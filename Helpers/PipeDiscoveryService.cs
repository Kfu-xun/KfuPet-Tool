using System.Diagnostics;
using System.IO;

namespace KfuPet_Tool.Helpers
{
    /// <summary>
    /// 命名管道发现服务，用于扫描系统中可用的 KfuPet 管道。
    /// </summary>
    public static class PipeDiscoveryService
    {
        private const string PipeRootPath = @"\\.\pipe\";
        private const string KfuPetPrefix = "KfuPet";
        private const string KfuPetProcessName = "KfuPet";

        /// <summary>
        /// 扫描系统中所有以 "KfuPet" 开头的命名管道。
        /// </summary>
        /// <returns>命中的管道名称列表（不含路径前缀）。</returns>
        public static List<string> DiscoverKfuPetPipes()
        {
            var result = new List<string>();
            try
            {
                var allPipes = Directory.GetFiles(PipeRootPath);
                foreach (var pipePath in allPipes)
                {
                    var name = pipePath;
                    if (pipePath.StartsWith(PipeRootPath, StringComparison.OrdinalIgnoreCase))
                    {
                        name = pipePath.Substring(PipeRootPath.Length);
                    }

                    if (name.StartsWith(KfuPetPrefix, StringComparison.OrdinalIgnoreCase))
                    {
                        result.Add(name);
                    }
                }
            }
            catch (Exception)
            {
            }
            return result;
        }

        /// <summary>
        /// 统计当前正在运行的 KfuPet 进程数量。
        /// 多个实例共享同一管道名会导致命令随机分配，需提示用户。
        /// </summary>
        public static int CountKfuPetProcesses()
        {
            try
            {
                return Process.GetProcessesByName(KfuPetProcessName).Length;
            }
            catch
            {
                return 0;
            }
        }
    }
}

