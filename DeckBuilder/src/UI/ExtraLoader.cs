using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using WeissDeckBuilder.Extras;

namespace WeissDeckBuilder.UI
{
    /// <summary>
    /// 一个插件的加载结果。<see cref="Plugin"/> 为 null 时 <see cref="Error"/>
    /// 说明原因，侧边栏仍然把它列出来（红字），这样用户能看到"装错了"，
    /// 而不是插件悄悄消失。
    /// </summary>
    internal sealed class ExtraEntry
    {
        public ExtraEntry(ExtraManifest manifest)
        {
            Manifest = manifest;
            Info = ExtraLoader.CreateInfo(manifest);
            Error = string.Empty;
        }

        /// <summary>plugin.txt 解析出来的清单。</summary>
        public ExtraManifest Manifest { get; private set; }

        /// <summary>交给插件的显示信息。</summary>
        public ExtraInfo Info { get; private set; }

        /// <summary>插件实例；加载失败时为 null。</summary>
        public IExtraPlugin Plugin { get; set; }

        /// <summary>交给插件的主程序接口，由 MainForm 在扫描后填上。</summary>
        public IExtraHost Host { get; set; }

        /// <summary>加载失败的原因；页面上会原样显示。</summary>
        public string Error { get; set; }

        /// <summary>true 表示这个插件没能加载，只能看错误页。</summary>
        public bool Failed
        {
            get { return Plugin == null; }
        }
    }

    /// <summary>
    /// 把 <see cref="ExtraCatalog.Scan"/> 找到的清单变成插件实例。
    /// 这一步是唯一会执行第三方代码（程序集静态构造、插件构造函数）的地方，
    /// 所以每个插件单独 try/catch：一个坏的 dll 不影响其他插件。
    ///
    /// 程序集用 <see cref="Assembly.LoadFrom"/> 加载，并直接使用主程序里已经
    /// 加载的那一份 <c>IExtraPlugin</c> 定义 —— 插件编译时引用
    /// <c>dist\WeissDeckBuilder.exe</c>，运行时 LoadFrom 会把引用绑到同一份
    /// 程序集上，所以不需要第二个"插件 SDK"dll。
    ///
    /// 代价是 dll 会被锁定：运行中替换插件文件会失败，升级插件要么关掉程序，
    /// 要么把新版本放进另一个文件夹。"重新扫描"只负责发现新文件夹。
    /// </summary>
    internal static class ExtraLoader
    {
        /// <summary>
        /// 扫描并加载 <paramref name="rootDirectory"/> 下的所有插件。
        /// 永远不抛异常；每个失败都会通过 <paramref name="log"/> 写一行。
        /// </summary>
        public static IList<ExtraEntry> Discover(string rootDirectory,
            Action<string> log)
        {
            IList<ExtraManifest> manifests = ExtraCatalog.Scan(rootDirectory);
            var entries = new List<ExtraEntry>(manifests.Count);
            for (int i = 0; i < manifests.Count; i++)
            {
                ExtraManifest manifest = manifests[i];
                var entry = new ExtraEntry(manifest);
                entries.Add(entry);

                for (int w = 0; w < manifest.Warnings.Count; w++)
                {
                    Report(log, "extra/" + manifest.Id + "：清单警告：" +
                        manifest.Warnings[w]);
                }

                if (!manifest.IsValid)
                {
                    entry.Error = manifest.Error;
                    Report(log, "extra/" + manifest.Id + "：清单不可用：" +
                        entry.Error);
                    continue;
                }

                string assemblyPath = manifest.AssemblyPath;
                if (string.IsNullOrEmpty(assemblyPath) ||
                    !File.Exists(assemblyPath))
                {
                    entry.Error = "找不到插件程序集：" + assemblyPath;
                    Report(log, "extra/" + manifest.Id + "：" + entry.Error);
                    continue;
                }

                try
                {
                    Assembly assembly = Assembly.LoadFrom(assemblyPath);
                    entry.Plugin = CreatePlugin(assembly, manifest);
                    if (entry.Plugin == null)
                    {
                        entry.Error = "程序集里没有找到 IExtraPlugin 实现：" +
                            assemblyPath;
                        Report(log, "extra/" + manifest.Id + "：" + entry.Error);
                    }
                }
                catch (Exception ex)
                {
                    entry.Error = "插件加载失败：" + ex.Message;
                    Report(log, "extra/" + manifest.Id + "：" + entry.Error);
                }
            }

            return entries;
        }

        /// <summary>把清单里的显示字段抄成插件看到的那一份。</summary>
        public static ExtraInfo CreateInfo(ExtraManifest manifest)
        {
            var info = new ExtraInfo();
            if (manifest == null)
                return info;

            info.Id = manifest.Id;
            info.Name = manifest.Name;
            info.Description = manifest.Description;
            info.Version = manifest.Version;
            info.Author = manifest.Author;
            info.FolderPath = manifest.FolderPath;
            info.AssemblyPath = manifest.AssemblyPath;
            return info;
        }

        /// <summary>
        /// 在程序集里找插件类型：清单写了 entry 就只认那一个，没写就让
        /// 第一个公开、可实例化的实现说了算。多个实现同时存在时按类型
        /// 全名排序，保证每次启动选中的都是同一个。
        /// </summary>
        private static IExtraPlugin CreatePlugin(Assembly assembly,
            ExtraManifest manifest)
        {
            if (!string.IsNullOrEmpty(manifest.EntryType))
            {
                Type declared = assembly.GetType(manifest.EntryType, false, true);
                if (declared == null)
                {
                    throw new InvalidOperationException(
                        "清单 entry 指定的类型不存在：" + manifest.EntryType);
                }

                return Activate(declared);
            }

            Type found = null;
            Type[] types = GetLoadableTypes(assembly);
            for (int i = 0; i < types.Length; i++)
            {
                Type type = types[i];
                if (!IsPluginType(type))
                    continue;

                if (found == null ||
                    string.CompareOrdinal(type.FullName, found.FullName) < 0)
                {
                    found = type;
                }
            }

            return found == null ? null : Activate(found);
        }

        private static IExtraPlugin Activate(Type type)
        {
            if (!IsPluginType(type))
            {
                throw new InvalidOperationException(
                    "entry 指向的类型不是可实例化的 IExtraPlugin：" +
                    type.FullName);
            }

            return (IExtraPlugin)Activator.CreateInstance(type);
        }

        private static bool IsPluginType(Type type)
        {
            return type != null && type.IsClass && !type.IsAbstract &&
                typeof(IExtraPlugin).IsAssignableFrom(type) &&
                type.GetConstructor(Type.EmptyTypes) != null;
        }

        /// <summary>
        /// 程序集里有些类型依赖别的 dll 没带过来时 GetTypes 会整体失败，
        /// 这里只丢掉加载不了的那几个类型，剩下的照常找。
        /// </summary>
        private static Type[] GetLoadableTypes(Assembly assembly)
        {
            try
            {
                return assembly.GetTypes();
            }
            catch (ReflectionTypeLoadException ex)
            {
                if (ex.Types == null)
                    return new Type[0];

                var loadable = new List<Type>();
                for (int i = 0; i < ex.Types.Length; i++)
                {
                    if (ex.Types[i] != null)
                        loadable.Add(ex.Types[i]);
                }

                return loadable.ToArray();
            }
        }

        private static void Report(Action<string> log, string message)
        {
            if (log != null)
                log(message);
        }
    }
}
