using System;
using System.Collections.Generic;
using System.IO;
using WuwaTappoCdpHost;

namespace WuwaTappoCdpHostTests
{
    internal static class CdpHostSelfTestProgram
    {
        private static int Main()
        {
            int passed = 0;
            try
            {
                string[] rootPaths = {
                    Path.GetPathRoot(AppDomain.CurrentDomain.BaseDirectory) ?? "C:\\",
                    @"C:\",
                    @"E:\"
                };
                foreach (string rootPath in rootPaths)
                {
                    if (!String.IsNullOrWhiteSpace(rootPath) &&
                        !String.IsNullOrWhiteSpace(CdpHostForm.SplitParent(rootPath)))
                    {
                        throw new InvalidOperationException(
                            "根目录没有终止：" + rootPath);
                    }
                    passed++;
                    Console.WriteLine("PASS 根目录终止 " + rootPath);
                }

                string current = AppDomain.CurrentDomain.BaseDirectory;
                HashSet<string> seen = new HashSet<string>(
                    StringComparer.OrdinalIgnoreCase);
                string parent = CdpHostForm.SplitParent(current);
                int depth = 0;
                while (!String.IsNullOrWhiteSpace(parent))
                {
                    if (!seen.Add(parent))
                    {
                        throw new InvalidOperationException(
                            "祖先路径出现循环：" + parent);
                    }
                    if (depth >= 32)
                    {
                        throw new InvalidOperationException(
                            "祖先路径超过 32 层仍未终止。");
                    }
                    parent = CdpHostForm.SplitParent(parent);
                    depth++;
                }
                passed++;
                Console.WriteLine(
                    "PASS 便携目录祖先遍历终止 depth=" + depth);

                Console.WriteLine("SUMMARY passed=" + passed + " failed=0");
                return 0;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine("FAIL " + ex);
                Console.WriteLine("SUMMARY passed=" + passed + " failed=1");
                return 1;
            }
        }
    }
}
