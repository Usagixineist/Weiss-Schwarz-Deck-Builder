using System;
using System.Collections.Generic;
using System.IO;

namespace WeissDeckBuilder.Extras
{
    /// <summary>
    /// extra\&lt;插件&gt;\plugin.txt 的解析结果。清单只是 INI 风格的文本，和
    /// settings.txt 用同一套写法（<c>键=值</c>、<c>#</c> 开头是注释），所以
    /// 不需要额外的解析库：读不懂的行跳过，没写的键全部由这里补默认值。
    ///
    /// 一个插件文件夹最少只要有一个 dll（默认按文件夹名找 <c>&lt;id&gt;.dll</c>）；
    /// 清单里的 name / description / version 只影响侧边栏和页头显示。
    /// </summary>
    public sealed class ExtraManifest
    {
        /// <summary>清单文件名。插件文件夹里必须叫这个名字才会被加载。</summary>
        public const string FileName = "plugin.txt";

        /// <summary>侧边栏排序用的默认值，数值小的排在前面。</summary>
        public const int DefaultOrder = 100;

        /// <summary>没写 assembly 时按插件 id 去找的程序集后缀。</summary>
        public const string DefaultAssemblySuffix = ".dll";

        /// <summary>id 的最大长度，避免出现无法显示的侧边栏条目。</summary>
        public const int MaxIdLength = 64;

        public ExtraManifest()
        {
            Id = string.Empty;
            Name = string.Empty;
            Description = string.Empty;
            Version = string.Empty;
            Author = string.Empty;
            Assembly = string.Empty;
            EntryType = string.Empty;
            FolderPath = string.Empty;
            Enabled = true;
            Order = DefaultOrder;
            IsValid = true;
            Error = string.Empty;
            Warnings = new List<string>();
        }

        /// <summary>插件标识，默认取文件夹名（改小写）。同一目录里 id 唯一。</summary>
        public string Id { get; set; }

        /// <summary>侧边栏和页头显示的名字，默认取文件夹名。</summary>
        public string Name { get; set; }

        /// <summary>页头副标题，也是侧边栏的悬停提示。</summary>
        public string Description { get; set; }

        /// <summary>版本号，只用于显示，不参与兼容性判断。</summary>
        public string Version { get; set; }

        /// <summary>作者，只用于显示。</summary>
        public string Author { get; set; }

        /// <summary>
        /// 插件程序集的文件名，相对插件文件夹。默认 <c>&lt;id&gt;.dll</c>；
        /// 只接受文件名，写路径会被拒绝，插件不能从别的地方加载代码。
        /// </summary>
        public string Assembly { get; set; }

        /// <summary>
        /// 插件入口类型全名。<c>entry=</c> 写了就只认这一个类型，没写时
        /// 自动在程序集里找第一个可实例化的 <see cref="IExtraPlugin"/>。
        /// </summary>
        public string EntryType { get; set; }

        /// <summary>插件文件夹的完整路径。</summary>
        public string FolderPath { get; set; }

        /// <summary>false 时完全不加载，文件夹留在那里当备份。</summary>
        public bool Enabled { get; set; }

        /// <summary>侧边栏排序，数值小的在上面。</summary>
        public int Order { get; set; }

        /// <summary>清单是否可用；false 时 <see cref="Error"/> 说明原因。</summary>
        public bool IsValid { get; set; }

        /// <summary>不可用的原因。</summary>
        public string Error { get; set; }

        /// <summary>解析时被跳过的行 / 未知键 / 被纠正的值，写进 extras.log。</summary>
        public IList<string> Warnings { get; private set; }

        /// <summary>插件程序集的完整路径；清单里写了非法文件名时返回空。</summary>
        public string AssemblyPath
        {
            get
            {
                if (string.IsNullOrEmpty(Assembly) ||
                    string.IsNullOrEmpty(FolderPath) ||
                    !IsValidAssemblyName(Assembly))
                {
                    return string.Empty;
                }

                return Path.Combine(FolderPath, Assembly);
            }
        }

        /// <summary>
        /// 解析一个插件文件夹的清单。清单不存在或读不出来时返回 null；
        /// 清单存在但内容有问题时返回一个 <see cref="IsValid"/> 为 false 的
        /// 清单，让侧边栏能把原因显示出来，而不是悄悄地什么都不出现。
        /// </summary>
        public static ExtraManifest ParseFile(string folderPath)
        {
            if (string.IsNullOrEmpty(folderPath))
                return null;

            string path = Path.Combine(folderPath, FileName);
            string[] lines;
            try
            {
                if (!File.Exists(path))
                    return null;
                lines = File.ReadAllLines(path);
            }
            catch (IOException)
            {
                return null;
            }
            catch (UnauthorizedAccessException)
            {
                return null;
            }
            catch (ArgumentException)
            {
                return null;
            }
            catch (NotSupportedException)
            {
                return null;
            }

            return Parse(folderPath, lines);
        }

        /// <summary>
        /// 解析清单内容。<paramref name="folderPath"/> 只用来补默认值
        /// （id / name / 程序集名），不会访问磁盘。
        /// </summary>
        public static ExtraManifest Parse(string folderPath, string[] lines)
        {
            string folderName = FolderNameOf(folderPath);
            var manifest = new ExtraManifest();
            manifest.FolderPath = folderPath == null ? string.Empty : folderPath;
            manifest.Id = folderName;
            manifest.Name = folderName;
            manifest.Assembly = folderName + DefaultAssemblySuffix;

            if (lines != null)
            {
                for (int i = 0; i < lines.Length; i++)
                {
                    string line = lines[i] == null ? string.Empty : lines[i].Trim();
                    if (line.Length == 0 || line[0] == '#' || line[0] == ';')
                        continue;

                    int separator = line.IndexOf('=');
                    if (separator <= 0)
                    {
                        manifest.Warnings.Add("第 " + (i + 1) +
                            " 行不是 键=值，已忽略：" + line);
                        continue;
                    }

                    string key = line.Substring(0, separator).Trim();
                    string value = line.Substring(separator + 1).Trim();
                    if (!manifest.Apply(key, value))
                    {
                        manifest.Warnings.Add("未知键 " + key + "，已忽略");
                    }
                }
            }

            manifest.Normalize();
            return manifest;
        }

        /// <summary>
        /// id 只能用小写字母、数字、<c>-</c> 和 <c>_</c>：它同时是文件夹名、
        /// 去重键和日志里的前缀，出现空格或中文会让这三处都对不上。
        /// 以 <c>_</c> 开头的文件夹留给模板，加载器不会扫描。
        /// </summary>
        public static bool IsValidId(string id)
        {
            if (string.IsNullOrEmpty(id) || id.Length > MaxIdLength)
                return false;

            for (int i = 0; i < id.Length; i++)
            {
                char c = id[i];
                bool allowed = (c >= 'a' && c <= 'z') ||
                    (c >= '0' && c <= '9') || c == '-' || c == '_';
                if (!allowed)
                    return false;
            }

            return true;
        }

        /// <summary>
        /// 程序集名只能是文件名：清单是用户可编辑的文本，写绝对路径或
        /// <c>..\</c> 就等于允许从插件目录外加载代码。
        /// </summary>
        public static bool IsValidAssemblyName(string name)
        {
            if (string.IsNullOrEmpty(name))
                return false;
            if (name.IndexOf("..", StringComparison.Ordinal) >= 0)
                return false;
            if (name.IndexOf(':') >= 0)
                return false;
            if (name.IndexOf('\\') >= 0 || name.IndexOf('/') >= 0)
                return false;
            return true;
        }

        public override string ToString()
        {
            return string.IsNullOrEmpty(Version) ? Name : Name + " " + Version;
        }

        /// <summary>把一行 键=值 写进清单；返回 false 表示这个键不认识。</summary>
        private bool Apply(string key, string value)
        {
            switch (key.ToLowerInvariant())
            {
                case "id":
                    Id = value;
                    return true;
                case "name":
                    Name = value;
                    return true;
                case "description":
                    Description = value;
                    return true;
                case "version":
                    Version = value;
                    return true;
                case "author":
                    Author = value;
                    return true;
                case "assembly":
                    Assembly = value;
                    return true;
                case "entry":
                    EntryType = value;
                    return true;
                case "enabled":
                {
                    bool enabled;
                    if (TryParseSwitch(value, out enabled))
                    {
                        Enabled = enabled;
                        return true;
                    }

                    Warnings.Add("enabled 只认 true/false，已按 true 处理：" + value);
                    return true;
                }
                case "order":
                {
                    int order;
                    if (int.TryParse(value, out order))
                    {
                        Order = order;
                        return true;
                    }

                    Warnings.Add("order 需要整数，已按 " + DefaultOrder +
                        " 处理：" + value);
                    return true;
                }
                default:
                    return false;
            }
        }

        /// <summary>补齐默认值、纠正 id 的写法，最后判定清单能不能用。</summary>
        private void Normalize()
        {
            if (string.IsNullOrEmpty(Name))
                Name = FolderNameOf(FolderPath);

            string fallbackId = string.IsNullOrEmpty(Id) ? "plugin" : Id;
            if (string.IsNullOrEmpty(Assembly))
            {
                Assembly = fallbackId + DefaultAssemblySuffix;
            }
            else if (!IsValidAssemblyName(Assembly))
            {
                Warnings.Add("assembly 只能是插件文件夹里的文件名，已改用 " +
                    fallbackId + DefaultAssemblySuffix + "：" + Assembly);
                Assembly = fallbackId + DefaultAssemblySuffix;
            }

            if (!IsValidId(Id))
            {
                string lowered = Id == null ? string.Empty : Id.ToLowerInvariant();
                if (IsValidId(lowered))
                {
                    Warnings.Add("id 已改成小写：" + lowered);
                    Id = lowered;
                }
            }

            if (!IsValidId(Id))
            {
                IsValid = false;
                Error = "id 只能由小写字母、数字、- 和 _ 组成（最多 " +
                    MaxIdLength + " 个字符）：" + (Id ?? string.Empty);
            }
        }

        private static bool TryParseSwitch(string value, out bool result)
        {
            result = true;
            if (string.IsNullOrEmpty(value))
                return false;

            string text = value.Trim();
            if (string.Equals(text, "true", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(text, "yes", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(text, "on", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(text, "1", StringComparison.Ordinal))
            {
                result = true;
                return true;
            }

            if (string.Equals(text, "false", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(text, "no", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(text, "off", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(text, "0", StringComparison.Ordinal))
            {
                result = false;
                return true;
            }

            return false;
        }

        private static string FolderNameOf(string folderPath)
        {
            if (string.IsNullOrEmpty(folderPath))
                return string.Empty;

            string trimmed = folderPath.TrimEnd(Path.DirectorySeparatorChar,
                Path.AltDirectorySeparatorChar);
            if (trimmed.Length == 0)
                return string.Empty;

            string name = Path.GetFileName(trimmed);
            return string.IsNullOrEmpty(name) ? trimmed : name;
        }
    }
}
