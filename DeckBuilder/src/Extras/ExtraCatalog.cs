using System;
using System.Collections.Generic;
using System.IO;

namespace WeissDeckBuilder.Extras
{
    /// <summary>
    /// 扫描 extra\ 目录，列出可加载的插件。这一层只看文件夹和 plugin.txt，
    /// 不反射、不加载 dll，所以能在没有插件的机器上安静地返回空列表；
    /// 真正加载程序集的是 UI 里的 <c>ExtraLoader</c>。
    /// </summary>
    public static class ExtraCatalog
    {
        /// <summary>
        /// 列出 <paramref name="rootDirectory"/> 下所有可加载插件，按
        /// order → 名字 → id 排序。以下情况会被跳过：
        /// 以 <c>_</c> 开头的文件夹（模板 / 备份）、没有 plugin.txt 的文件夹、
        /// <c>enabled=false</c> 的插件。同 id 的两个文件夹只保留 order 小的
        /// 那个，另一个的路径写进保留者的 Warnings。
        /// </summary>
        public static IList<ExtraManifest> Scan(string rootDirectory)
        {
            var result = new List<ExtraManifest>();
            if (string.IsNullOrEmpty(rootDirectory))
                return result;

            string[] folders;
            try
            {
                if (!Directory.Exists(rootDirectory))
                    return result;
                folders = Directory.GetDirectories(rootDirectory);
            }
            catch (IOException)
            {
                return result;
            }
            catch (UnauthorizedAccessException)
            {
                return result;
            }
            catch (ArgumentException)
            {
                return result;
            }
            catch (NotSupportedException)
            {
                return result;
            }

            // 文件夹顺序在资源管理器里看起来是随机的，先排序再解析，同 order
            // 的插件在每台机器上都能排出同样的顺序。
            Array.Sort(folders, StringComparer.OrdinalIgnoreCase);

            var byId = new Dictionary<string, ExtraManifest>(StringComparer.Ordinal);
            for (int i = 0; i < folders.Length; i++)
            {
                ExtraManifest manifest = ReadFolder(folders[i]);
                if (manifest == null)
                    continue;

                ExtraManifest existing;
                if (byId.TryGetValue(manifest.Id, out existing))
                {
                    bool keepNew = manifest.Order < existing.Order;
                    ExtraManifest keep = keepNew ? manifest : existing;
                    ExtraManifest drop = keepNew ? existing : manifest;
                    keep.Warnings.Add("id “" + manifest.Id +
                        "”与另一个插件重复，已忽略：" + drop.FolderPath);
                    if (keepNew)
                    {
                        result.Remove(existing);
                        result.Add(manifest);
                        byId[manifest.Id] = manifest;
                    }

                    continue;
                }

                byId[manifest.Id] = manifest;
                result.Add(manifest);
            }

            result.Sort(Compare);
            return result;
        }

        /// <summary>侧边栏顺序：order，然后显示名，最后 id。</summary>
        public static int Compare(ExtraManifest left, ExtraManifest right)
        {
            if (ReferenceEquals(left, right))
                return 0;
            if (left == null)
                return -1;
            if (right == null)
                return 1;

            int order = left.Order.CompareTo(right.Order);
            if (order != 0)
                return order;

            int name = string.Compare(left.Name, right.Name,
                StringComparison.CurrentCultureIgnoreCase);
            if (name != 0)
                return name;

            return string.Compare(left.Id, right.Id, StringComparison.Ordinal);
        }

        /// <summary>读一个文件夹的清单；不值得列出来的文件夹返回 null。</summary>
        private static ExtraManifest ReadFolder(string folder)
        {
            string name = Path.GetFileName(folder);
            if (string.IsNullOrEmpty(name) ||
                name.StartsWith("_", StringComparison.Ordinal))
            {
                return null;
            }

            if (!File.Exists(Path.Combine(folder, ExtraManifest.FileName)))
                return null;

            ExtraManifest manifest = ExtraManifest.ParseFile(folder);
            if (manifest == null || !manifest.Enabled)
                return null;

            return manifest;
        }
    }
}
