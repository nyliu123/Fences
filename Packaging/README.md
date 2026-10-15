# 自解压发行包

运行生成的 `Fences-<版本>-win-x64-sfx.exe`，选择目标目录并解压，再运行目录中的 `Fences.exe`。解压器不会自动启动 Fences，不会导入打包机器上的个人配置。

## 构建

准备官方 7-Zip x64 工具目录，其中应包含 `7z.exe`、`7z.dll`、`7z.sfx` 和 `License.txt`。可以使用已安装的 7-Zip，也可以从[官方页面](https://www.7-zip.org/download.html)下载并解压对应发行文件，无需将打包工具随 Fences 分发。

在 Visual Studio 开发者 PowerShell 中运行：

```powershell
.\Packaging\Build-SelfExtractingRelease.ps1 -SevenZipDirectory 'C:\Program Files\7-Zip'
```

也可通过 `-MsBuildPath` 指定 `MSBuild.exe`。脚本先编译 Release，再从明确的文件清单创建自解压包，输出到 `artifacts`。

包内只包含主程序、恢复程序、默认运行配置、语言资源、调试符号、项目文档与许可证，以及自解压模块的第三方许可信息。`Fences.exe.config` 和 `Fences.Recovery.exe.config` 来自源码中的 `App.config`；它们是程序运行所需的默认设置，不是分区配置。脚本会检查二者与源码默认配置的哈希一致。

已有的 `__fence_metadata.xml`、分区项目、日志及 `%LocalAppData%` 下的内容不会加入发行包。Fences 启动后仍按项目文档使用 `%LocalAppData%\Fences`，旧目录迁移逻辑也保持不变。
