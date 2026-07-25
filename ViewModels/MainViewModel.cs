using System.Collections.ObjectModel;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using KfuPet.Ipc.Client;
using KfuPet_Tool.Helpers;
using KfuPet_Tool.Models;

namespace KfuPet_Tool.ViewModels
{
    public partial class MainViewModel : ObservableObject
    {
        private SkeletonPipeClient _pipeClient;

        [ObservableProperty]
        private ObservableCollection<BoneInfo> _rootBones = new();

        [ObservableProperty]
        private BoneInfo? _selectedBone;

        [ObservableProperty]
        private bool _isConnected;

        [ObservableProperty]
        private string _statusMessage = "未连接";

        [ObservableProperty]
        private ObservableCollection<string> _availablePipes = new();

        [ObservableProperty]
        private string? _selectedPipe;

        [ObservableProperty]
        private bool _isLoading;

        [ObservableProperty]
        private double _canvasWidth = 400;

        [ObservableProperty]
        private double _canvasHeight = 400;

        public event EventHandler? PreviewUpdated;

        public MainViewModel()
        {
            _pipeClient = new SkeletonPipeClient();
            ScanPipes();
        }

        partial void OnIsConnectedChanged(bool value)
        {
            PreviewUpdated?.Invoke(this, EventArgs.Empty);
        }

        partial void OnRootBonesChanged(ObservableCollection<BoneInfo> value)
        {
            PreviewUpdated?.Invoke(this, EventArgs.Empty);
        }

        partial void OnSelectedPipeChanged(string? value)
        {
            if (!string.IsNullOrEmpty(value))
            {
                _pipeClient = new SkeletonPipeClient(value);
                StatusMessage = $"已选择管道：{value}";
            }
        }

        private void RaisePreviewUpdated()
        {
            PreviewUpdated?.Invoke(this, EventArgs.Empty);
        }

        [RelayCommand]
        private void ScanPipes()
        {
            var pipes = PipeDiscoveryService.DiscoverKfuPetPipes();
            int processCount = PipeDiscoveryService.CountKfuPetProcesses();

            AvailablePipes.Clear();
            foreach (var pipe in pipes)
            {
                AvailablePipes.Add(pipe);
            }

            if (pipes.Count == 0)
            {
                SelectedPipe = null;
                StatusMessage = "未发现 KfuPet 管道，请确认 KfuPet 已运行";
            }
            else if (pipes.Count == 1)
            {
                SelectedPipe = pipes[0];
                if (processCount > 1)
                {
                    StatusMessage = $"已识别管道：{pipes[0]}（警告：检测到 {processCount} 个 KfuPet 进程，命令可能随机分配到不同实例，建议只保留一个）";
                }
                else
                {
                    StatusMessage = $"已自动识别管道：{pipes[0]}";
                }
            }
            else
            {
                SelectedPipe = null;
                StatusMessage = $"发现 {pipes.Count} 个管道，请手动选择";
            }
        }

        [RelayCommand]
        private async Task ConnectAsync()
        {
            if (IsLoading) return;

            if (string.IsNullOrEmpty(SelectedPipe))
            {
                StatusMessage = "请先选择一个管道";
                return;
            }

            IsLoading = true;
            try
            {
                var boneIds = await Task.Run(() => _pipeClient.GetBoneIds());
                if (boneIds.Count > 0)
                {
                    IsConnected = true;
                    StatusMessage = $"已连接：{SelectedPipe}";
                    await LoadBoneTreeAsync();
                }
                else
                {
                    StatusMessage = "连接失败：未获取到骨骼数据";
                }
            }
            catch (Exception ex)
            {
                IsConnected = false;
                StatusMessage = $"连接失败：{ex.Message}";
                MessageBox.Show($"连接失败：{ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                IsLoading = false;
            }
        }

        [RelayCommand]
        private void Disconnect()
        {
            IsConnected = false;
            StatusMessage = "已断开";
            RootBones.Clear();
            SelectedBone = null;
        }

        [RelayCommand]
        private async Task RefreshAsync()
        {
            if (!IsConnected) return;
            await LoadBoneTreeAsync();
        }

        private async Task LoadBoneTreeAsync()
        {
            try
            {
                var boneIds = await Task.Run(() => _pipeClient.GetBoneIds());
                var boneDict = new Dictionary<string, BoneInfo>();

                foreach (var id in boneIds)
                {
                    var bone = new BoneInfo { BoneId = id };
                    boneDict[id] = bone;
                }

                foreach (var bone in boneDict.Values)
                {
                    bone.BoneName = await Task.Run(() => _pipeClient.GetBoneName(bone.BoneId) ?? bone.BoneId);
                    bone.ParentBoneId = await Task.Run(() => _pipeClient.GetParentBoneId(bone.BoneId));

                    var pos = await Task.Run(() => _pipeClient.GetPosition(bone.BoneId));
                    if (pos.HasValue)
                    {
                        bone.PositionX = pos.Value.X;
                        bone.PositionY = pos.Value.Y;
                    }

                    bone.Rotation = await Task.Run(() => _pipeClient.GetRotation(bone.BoneId)) ?? 0;

                    var scale = await Task.Run(() => _pipeClient.GetScale(bone.BoneId));
                    if (scale.HasValue)
                    {
                        bone.ScaleX = scale.Value.X;
                        bone.ScaleY = scale.Value.Y;
                    }

                    bone.IsActive = await Task.Run(() => _pipeClient.IsActive(bone.BoneId)) ?? true;

                    var worldPos = await Task.Run(() => _pipeClient.GetWorldPosition(bone.BoneId));
                    if (worldPos.HasValue)
                    {
                        bone.WorldX = worldPos.Value.X;
                        bone.WorldY = worldPos.Value.Y;
                    }
                }

                RootBones.Clear();
                foreach (var bone in boneDict.Values)
                {
                    if (string.IsNullOrEmpty(bone.ParentBoneId))
                    {
                        RootBones.Add(bone);
                    }
                    else if (boneDict.TryGetValue(bone.ParentBoneId, out var parent))
                    {
                        parent.Children.Add(bone);
                    }
                }
                RaisePreviewUpdated();
            }
            catch (Exception ex)
            {
                StatusMessage = $"加载骨骼树失败：{ex.Message}";
                MessageBox.Show($"加载骨骼树失败：{ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        [RelayCommand]
        private async Task SetPositionAsync()
        {
            if (SelectedBone == null || !IsConnected) return;

            try
            {
                await Task.Run(() => _pipeClient.SetPosition(SelectedBone.BoneId, SelectedBone.PositionX, SelectedBone.PositionY));
                await UpdateWorldPositionAsync(SelectedBone);
                RaisePreviewUpdated();
            }
            catch (Exception ex)
            {
                StatusMessage = $"设置位置失败：{ex.Message}";
            }
        }

        [RelayCommand]
        private async Task SetRotationAsync()
        {
            if (SelectedBone == null || !IsConnected) return;

            try
            {
                await Task.Run(() => _pipeClient.SetRotation(SelectedBone.BoneId, SelectedBone.Rotation));
                await RefreshAllWorldPositionsAsync();
                RaisePreviewUpdated();
            }
            catch (Exception ex)
            {
                StatusMessage = $"设置旋转失败：{ex.Message}";
            }
        }

        [RelayCommand]
        private async Task SetScaleAsync()
        {
            if (SelectedBone == null || !IsConnected) return;

            try
            {
                await Task.Run(() => _pipeClient.SetScale(SelectedBone.BoneId, SelectedBone.ScaleX, SelectedBone.ScaleY));
                await RefreshAllWorldPositionsAsync();
                RaisePreviewUpdated();
            }
            catch (Exception ex)
            {
                StatusMessage = $"设置缩放失败：{ex.Message}";
            }
        }

        [RelayCommand]
        private async Task SetActiveAsync()
        {
            if (SelectedBone == null || !IsConnected) return;

            try
            {
                await Task.Run(() => _pipeClient.SetActive(SelectedBone.BoneId, SelectedBone.IsActive));
                RaisePreviewUpdated();
            }
            catch (Exception ex)
            {
                StatusMessage = $"设置激活状态失败：{ex.Message}";
            }
        }

        [RelayCommand]
        private async Task ResetBoneAsync()
        {
            if (SelectedBone == null || !IsConnected) return;

            try
            {
                await Task.Run(() => _pipeClient.ResetBone(SelectedBone.BoneId));
                await RefreshBoneAsync(SelectedBone);
                await RefreshAllWorldPositionsAsync();
                RaisePreviewUpdated();
            }
            catch (Exception ex)
            {
                StatusMessage = $"重置骨骼失败：{ex.Message}";
            }
        }

        [RelayCommand]
        private async Task ResetAllAsync()
        {
            if (!IsConnected) return;

            var result = MessageBox.Show("确定要重置所有骨骼吗？", "确认", MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (result != MessageBoxResult.Yes) return;

            try
            {
                await Task.Run(() => _pipeClient.ResetAll());
                await RefreshAsync();
                RaisePreviewUpdated();
            }
            catch (Exception ex)
            {
                StatusMessage = $"重置所有骨骼失败：{ex.Message}";
            }
        }

        private async Task RefreshBoneAsync(BoneInfo bone)
        {
            var pos = await Task.Run(() => _pipeClient.GetPosition(bone.BoneId));
            if (pos.HasValue)
            {
                bone.PositionX = pos.Value.X;
                bone.PositionY = pos.Value.Y;
            }

            bone.Rotation = await Task.Run(() => _pipeClient.GetRotation(bone.BoneId)) ?? 0;

            var scale = await Task.Run(() => _pipeClient.GetScale(bone.BoneId));
            if (scale.HasValue)
            {
                bone.ScaleX = scale.Value.X;
                bone.ScaleY = scale.Value.Y;
            }

            bone.IsActive = await Task.Run(() => _pipeClient.IsActive(bone.BoneId)) ?? true;
        }

        private async Task UpdateWorldPositionAsync(BoneInfo bone)
        {
            var worldPos = await Task.Run(() => _pipeClient.GetWorldPosition(bone.BoneId));
            if (worldPos.HasValue)
            {
                bone.WorldX = worldPos.Value.X;
                bone.WorldY = worldPos.Value.Y;
            }

            foreach (var child in bone.Children)
            {
                await UpdateWorldPositionAsync(child);
            }
        }

        private async Task RefreshAllWorldPositionsAsync()
        {
            foreach (var bone in RootBones)
            {
                await UpdateWorldPositionAsync(bone);
            }
        }

        public IEnumerable<BoneInfo> GetAllBones()
        {
            foreach (var bone in RootBones)
            {
                yield return bone;
                foreach (var child in GetAllBones(bone))
                {
                    yield return child;
                }
            }
        }

        private IEnumerable<BoneInfo> GetAllBones(BoneInfo parent)
        {
            foreach (var child in parent.Children)
            {
                yield return child;
                foreach (var grandChild in GetAllBones(child))
                {
                    yield return grandChild;
                }
            }
        }
    }
}
