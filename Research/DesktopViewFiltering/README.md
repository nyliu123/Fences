# 桌面项目原地保留与图标过滤研究

本文保留研究阶段的实验记录。正式 1.7.0 已接入 `Fences.Native`，使用桌面模式并补齐“显示桌面图标”等菜单入口；当前产品行为见 [版本记录](../../CHANGELOG.md)，集成验证见 [测试说明](../../Tests/README.md)。

验证日期：2026-10-21。测试环境：Windows 11 专业版，10.0.26200，x64。

## 结论

**关键机制已经在隔离窗口中跑通：文件留在原路径，指定项目只在一个原生 Shell 视图中不可见，其余图标自动补位，普通文件视图及原生文件选择窗口仍能访问它。**

这还不是正式版桌面功能。成功的对象是程序自己托管的 `IExplorerBrowser` 视图；对当前 Explorer 桌面仅做了只读接口查询，没有设置过滤器、调整图标、修改显示设置或重启 Explorer。现有 Fences 源码与发行包没有因为这次研究而改动。

不能再笼统地说“Windows 无法做到只隐藏图标”；准确的界限是：自托管原生视图提供所需过滤能力，本机现有桌面视图没有暴露本次检查的对应接口。

## 实际结果

`Build-And-Run.ps1` 编译独立的 C++/Win32 程序，本机自动完成 45 项断言并退出（包括检测到的 7-Zip 扩展子菜单）。实验使用随机 GUID 临时目录，所有样本都由实验程序创建和清理。

| 检查 | 实测结果 |
| --- | --- |
| 同目录双视图 | 普通视图 5 个图标，过滤视图 4 个；指定文件仅在过滤视图中消失。 |
| 无空隙排列 | 读取原生图标坐标，过滤后的项目占据未过滤视图的前 4 个网格位置。 |
| 文件原地保留 | 原路径可直接读取，卷标识与文件 ID 相同；名称、属性、创建时间、修改时间、大小未改变。 |
| 文件选择 | 实际打开 Windows 原生文件选择窗口，通过其文件名控件确认原路径，成功返回该路径；过滤图标仍保持隐藏。 |
| 刷新 | 连续 3 次刷新，过滤仍有效，普通视图仍显示目标。 |
| 创建文件 | Shell 文件创建通知使两个视图显示新文件，已过滤图标没有重新出现。 |
| 撤销及重新应用 | 取消过滤后全部项目重新出现，再安装过滤器可重新隐藏目标，无须移动文件。 |
| 视图重建 | 销毁并重新创建自托管视图，再挂接过滤器，目标继续隐藏。 |
| 隐藏项目 | 实验枚举显式包含隐藏文件；隐藏对照文件可见，指定的普通文件仍被排除。 |
| 文件夹与快捷方式 | 同一过滤机制可排除文件夹或 `.lnk`；目录内容、快捷方式和实际目标仍留在原处。 |
| 旧版移除消息 | 在本进程拥有的 Shell 视图发送 `SFVM_REMOVEOBJECT`，返回值为 0，图标数量仍为 5，未产生移除效果。 |

“视图重建”不是实际 Explorer 重启或系统重启测试。“文件选择”使用同进程的独立原生对话框，不代表已经测试每个浏览器、Office 或其他应用的文件导航实现。

右键菜单的补充实验已显示文件菜单、第三方扩展子菜单和组合后的桌面空白菜单，实际执行了查看、排序、刷新、新建文件夹及快捷方式打开命令。它验证的是经典 Shell 菜单；新版 Windows 11 首层菜单及“显示桌面图标”等完整桌面行为尚未接入。[完整结果与限制](ContextMenus.md)。

本机真实桌面只读查询结果：

| 接口 | 当前桌面视图 | 当前桌面浏览器 |
| --- | --- | --- |
| `IFolderView2` | `S_OK` | `E_NOINTERFACE` |
| `IFolderFilterSite` | `E_NOINTERFACE` | `E_NOINTERFACE` |
| 旧版 `IShellFolderView` | `E_NOINTERFACE` | `E_NOINTERFACE` |

这只是当前系统上这几个接口的结果，不证明其他版本或所有内部接口都没有其他入口。

## 原型如何工作

- 两个 `IExplorerBrowser` 指向同一个测试目录，使用系统提供的图标视图和自动排列。
- 仅右侧浏览器安装 `IFolderFilterSite::SetFilter`。`IFolderFilter::ShouldShow` 按完整文件路径返回 `S_FALSE`，阻止指定项目进入该视图。
- 文件系统中不进行移动、改名、重解析点替换或目标文件隐藏属性修改。文件选择窗口使用自己的视图，不安装这个过滤器。
- 使用 `EBO_NOPERSISTVIEWSTATE`，不把实验视图状态保存为用户的文件夹视图设置。真实桌面的 COM 对象只用于能力查询。
- 文件选择窗口的测试定时器只操作本测试的对话框，并有超时取消；原型退出时销毁浏览器、窗口和临时文件。

实验的 `GetEnumFlags` 强制包含隐藏文件，仅为验证“按成员过滤”独立于文件隐藏属性。正式实现必须按用户原有的隐藏文件显示设置决定枚举范围，不能直接照搬这个实验参数。

## 适合继续推进的实现方向

优先研究在现有分区窗口下方托管一个经过过滤的原生桌面视图，而不是注入 Explorer 或改用文件隐藏属性。原生视图可能保留一部分 Shell 交互，不能据此假定所有桌面行为都会自动一致。

正式接入前仍需分别验证：

1. **真实桌面命名空间：** 合并用户与公共桌面，保留回收站等虚拟项目及用户原有显示规则。当前原型仅测试普通目录。
2. **桌面集成：** 正确处理原生桌面图标层、壁纸、窗口层级、焦点、Win+D 和任务栏；需要实际渲染验收。
3. **多屏与 DPI：** 负坐标、混合缩放、主屏切换、屏幕插拔、每屏工作区及分区占用区域，不能只靠一次自动排列。
4. **交互完整性：** 双击、打开位置、右键、拖放、重命名、删除、键盘选择、第三方 Shell 扩展和辅助功能。
5. **路径成员管理：** 分区以原路径记录成员；外部改名、删除、移动及公共桌面权限变化需要同步。已有存储目录中的项目需要可恢复的迁移，冲突时不能保证原名称自动可用。
6. **退出和异常恢复：** 正常退出时先关分区窗口，再恢复原桌面图标显示；崩溃或 Explorer 重启也要恢复。只能撤销本程序所做的视图变更，保留用户启动前的设置。
7. **用户状态：** 设计桌面图标顺序与位置的保存、补位和退出恢复规则，并覆盖重启后的持久化。

以上环节验证后，才适合用新模式替换现有“移动到存储目录、文件窗口期间暂时恢复”的行为。本次没有调整现有配置格式或重新打包正式版。

## 复现

依赖 Visual Studio C++ x64 Build Tools 与 Windows SDK，不依赖第三方库。项目当前 C# 程序不需要引用这个实验。

从仓库根目录运行：

```powershell
./Research/DesktopViewFiltering/Build-And-Run.ps1
```

脚本通过 `vswhere` 查找构建工具，编译时临时导入编译器环境，结束后恢复环境变量。需要指定工具链时可传入 `-VcVarsPath`。

产物位于忽略的 `Fences/obj/DesktopViewFilteringResearch`：

- `DesktopViewFilteringProbe.exe`：自动运行并关闭的研究程序。
- `results.txt`：只读能力查询、测试断言及清理结果。
- `filtered-native-view.png`：真实双视图窗口的截图，用于视觉检查；不是设计稿。
- `file-context-menu.png`、`file-7zip-submenu.png`：项目菜单及本机已安装的 7-Zip 子菜单。
- `folder-background-menu.png`、`desktop-background-menu.png`：分别由托管视图、桌面文件夹提供的菜单。
- `combined-desktop-menu.png`、`combined-view-submenu.png`、`combined-new-submenu.png`：组合后的经典桌面菜单及其子菜单。
- `*-menu.txt`：对应的原生菜单项、命令标识及可用状态清单。

## 参考资料

- [IExplorerBrowser](https://learn.microsoft.com/en-us/windows/win32/api/shobjidl_core/nn-shobjidl_core-iexplorerbrowser)：宿主、浏览器过滤器及与 Shell 视图的关系。
- [IFolderFilter::ShouldShow](https://learn.microsoft.com/en-us/windows/win32/api/shobjidl_core/nf-shobjidl_core-ifolderfilter-shouldshow)：按项目决定是否进入视图。
- [IFolderFilter::GetEnumFlags](https://learn.microsoft.com/en-us/windows/win32/api/shobjidl_core/nf-shobjidl_core-ifolderfilter-getenumflags)：指定枚举范围。
- [IShellFolderView](https://learn.microsoft.com/en-us/windows/win32/api/shlobj_core/nn-shlobj_core-ishellfolderview)：旧接口的兼容性限制。
- [SHShellFolderView_Message](https://learn.microsoft.com/en-us/windows/win32/api/shlobj_core/nf-shlobj_core-shshellfolderview_message)：旧视图消息及兼容性警告。
- [桌面图标定位接口说明](https://devblogs.microsoft.com/oldnewthing/20211122-00/?p=105948)：直接操作 Explorer 的 ListView 可能无法正确保留位置，应使用 Shell 接口。
