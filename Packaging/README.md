# 自解压发行包

运行生成的 `Fences-<版本>-win-x64-sfx.exe`，默认解压目录为 `C:\Program Files\Fences`，也可以手动选择其他目录。自解压程序会请求管理员权限，以便写入 Program Files；解压后运行目录中的 `Fences.exe`。主程序仍使用普通用户权限，自解压程序不会自动启动它，也不会导入打包机器上的个人配置。

## 构建

准备官方 7-Zip x64 工具目录，其中应包含 `7z.exe`、`7z.dll` 和 `License.txt`，以及同版本的完整 7-Zip 源码。可以使用已安装的 7-Zip，也可以从[官方页面](https://www.7-zip.org/download.html)下载并解压发行文件与源码。构建需要 Visual Studio C++ Build Tools 和 Windows SDK；用户运行最终自解压程序不需要安装 7-Zip 或 C++ 工具。

在 Visual Studio 开发者 PowerShell 中运行：

```powershell
.\Packaging\Build-FencesSfxModule.ps1 -SevenZipSourceDirectory 'D:\Tools\7zip-source'
.\Packaging\Build-SelfExtractingRelease.ps1 -SevenZipDirectory 'C:\Program Files\7-Zip'
```

第一步复制上游源码到 `Fences/obj/SfxModule` 下的独立工作目录，仅修改默认路径及自解压模块的权限清单，再编译原生 x64 模块。可用 `-DefaultDirectory` 调整打包时的默认路径；默认值为 `C:\Program Files\Fences`。原有的目标目录输入框、浏览按钮和 `-o` 显式路径参数保持可用。

第二步可通过 `-MsBuildPath` 指定 `MSBuild.exe`，通过 `-SfxModulePath` 指定定制模块；默认使用第一步生成的 `Fences/obj/SfxModule/7z.sfx`。脚本先检查模块权限清单，再编译 Release，并从明确的文件清单创建自解压包，输出到 `artifacts`。

模块构建脚本的 `-ExecutionLevel asInvoker` 仅生成独立的 `7z-test.sfx` 测试副本，用于在不自动操作 UAC 的情况下检查默认目录和解压逻辑。发行打包会拒绝该测试副本；实际发行模块使用 `requireAdministrator`，Fences 主程序的 `asInvoker` 清单不变。修改后的 7-Zip 模块附带上游许可、源码链接和重建脚本。

包内只包含主程序、恢复程序、默认运行配置、语言资源、调试符号、项目文档与许可证，以及自解压模块的第三方许可信息。`Fences.exe.config` 和 `Fences.Recovery.exe.config` 来自源码中的 `App.config`；它们是程序运行所需的默认设置，不是分区配置。脚本会检查二者与源码默认配置的哈希一致。

已有的 `__fence_metadata.xml`、分区项目、日志及 `%LocalAppData%` 下的内容不会加入发行包。Fences 启动后仍按项目文档使用 `%LocalAppData%\Fences`，旧目录迁移逻辑也保持不变。
