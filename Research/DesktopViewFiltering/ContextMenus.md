# 原生右键菜单接入实验

本文描述接入正式版之前的原型。正式 1.7.0 的桌面模式已补齐原生查看选项和“显示桌面图标”，并接入实际右键消息；当前实现位于 `Fences.Native`，参见 [版本记录](../../CHANGELOG.md)。

2026-10-21，Windows 11 专业版 10.0.26200，x64。

## 结论

**原地文件过滤方案能够复用经典 Shell 项目菜单，也能组合出包含视图控制、桌面专属命令和第三方扩展的空白菜单。核心命令已在隔离窗口中执行成功。**

原型没有替换真实桌面，没有修改 Fences 正式版或重新生成发行包。菜单由系统接口生成，实验窗口负责展示及命令路由；没有用文字列表模拟原生菜单。

## 验证结果

本机共通过 45 项断言，包括先前的路径保留、图标过滤和补位检查。编译使用 C++17、`/W4`、`/utf-8`，本次没有编译警告。实际打开了菜单和相关子菜单，截图已逐一检查。

| 范围 | 已观察或执行的结果 |
| --- | --- |
| 项目菜单 | 打开、删除、重命名、属性等原生菜单项正常出现。构建时提供 `CMF_CANRENAME`，否则系统不添加重命名项。 |
| 第三方项目扩展 | 本机 WinRAR、7-Zip、WPS 等菜单项出现；7-Zip 子菜单实际展开并截图。没有调用上传、分享、压缩等扩展动作。 |
| 视图空白菜单 | 系统提供查看、排序、分组、刷新、新建等菜单及子菜单。 |
| 查看命令 | 通过原生菜单命令 ID 执行“小图标”，读取视图模式和图标大小确认变化，再恢复原值。 |
| 排序命令 | 执行“递减”，读取原生排序方向确认变化，再恢复原排序。 |
| 刷新命令 | 原生命令执行成功，过滤视图仍保留应显示的 4 个项目。 |
| 桌面专属项 | 原生桌面文件夹菜单中显示“显示设置”“个性化”、Windows 聚焦背景选项及本机 NVIDIA 等扩展；未执行这些命令。 |
| 组合菜单 | 把视图控制和桌面菜单组合，保留原生项目、命令 ID、图标、子菜单及消息处理。 |
| 新建文件夹 | 组合菜单的新建组绑定到测试目录；通过组合菜单的命令分发实际创建文件夹，确认路径位于隔离目录，然后清理。 |
| 快捷方式打开 | 原生菜单的“打开”启动独立测试进程，参数中的临时结果文件成功产生；确认辅助进程退出后清理。 |
| 过滤状态 | 菜单操作结束后，普通视图回到 5 项，过滤视图回到 4 项，指定项目仍不显示。文件内容与身份检查继续通过。 |

## 接入方式

`ContextMenuProbe.cpp` 单独负责菜单桥接，`Probe.cpp` 继续负责文件过滤及窗口测试，二者不进入 Fences 产品构建。

1. 项目菜单由测试目录的 `IShellFolder::GetUIObjectOf(IID_IContextMenu)` 获取。
2. 视图控制由托管视图的 `IShellView::GetItemObject(SVGIO_BACKGROUND, IID_IContextMenu)` 获取。
3. 桌面专属项通过当前进程中的 `SHGetDesktopFolder` 和 `CreateViewObject(IID_IContextMenu)` 获取。
4. 视图菜单使用 `0x1000` 起的命令范围，桌面菜单使用 `0x4000` 起的范围，避免命令 ID 冲突。组合菜单保留原生菜单结构，根据范围分发给原来的 `IContextMenu::InvokeCommand`。
5. 绘制及子菜单初始化消息根据来源转发给对应的 `IContextMenu3` / `IContextMenu2`。界面截图使用真实 Win32 弹出菜单。

直接从 Explorer 的桌面视图获取 `IContextMenu` 在本机返回 `S_OK`，但在本进程菜单句柄上调用其 `QueryContextMenu` 返回 `E_INVALIDARG`。因此没有依靠这个远程菜单对象完成接入，而是在本进程创建桌面文件夹的菜单对象。

实验中的桌面专属项保留其原始处理器，但不执行。新建组则明确换成测试视图提供的组：它实际写入临时目录，避免实验在真实桌面上创建项目。以后接入真正的桌面视图时，命令目标与视图来源必须对应，不能只拼接菜单外观。

## 尚未完成的部分

- **不是 Windows 11 新版首层菜单。** 当前是 `IContextMenu` 经典菜单体系，对应“显示更多选项”一类的外观与扩展机制。
- **不是现有桌面菜单的完整复刻。** 当前“查看”来自普通原生文件视图，包含列表、详细信息等选项；桌面特有的“显示桌面图标”开关尚未桥接。
- **不是正式桌面上的右键交互接管。** 实验调用原生弹出菜单并测试命令分发，尚未替换真实桌面的右键消息入口。
- **没有逐项执行所有命令。** 删除、重命名等已验证菜单项存在；显示设置、个性化、聚焦背景及所有第三方扩展动作没有逐项执行。不能据此宣称所有 Shell 扩展完全兼容。
- **没有验证全部输入与生命周期。** 键盘、屏幕阅读器、多显示器、混合 DPI、Explorer 重启和异常恢复仍属于后续桌面集成工作。

## 复现与产物

从仓库根目录运行：

```powershell
./Research/DesktopViewFiltering/Build-And-Run.ps1
```

程序会依次显示短暂的原生菜单预览，自动关闭；所有文件操作使用随机 GUID 测试目录。结果和截图位于 `Fences/obj/DesktopViewFilteringResearch`，不会写入发行包。没有修改剪贴板，也没有调用真实桌面的查看、排序、新建或设置命令。

重点截图为 `combined-desktop-menu.png`、`combined-view-submenu.png`、`combined-new-submenu.png`、`file-context-menu.png`、`file-7zip-submenu.png`。文本清单记录菜单名称、命令标识和可用状态，用于核对实际返回内容。

## 参考资料

- [SVGIO_BACKGROUND](https://learn.microsoft.com/en-us/windows/win32/api/shobjidl_core/ne-shobjidl_core-_svgio)：获取视图空白处菜单。
- [IShellFolder::GetUIObjectOf](https://learn.microsoft.com/en-us/windows/win32/api/shobjidl_core/nf-shobjidl_core-ishellfolder-getuiobjectof)：获取项目菜单。
- [IShellFolder::CreateViewObject](https://learn.microsoft.com/en-us/windows/win32/api/shobjidl_core/nf-shobjidl_core-ishellfolder-createviewobject)：获取文件夹本身的菜单。
- [IContextMenu::InvokeCommand](https://learn.microsoft.com/en-us/windows/win32/api/shobjidl_core/nf-shobjidl_core-icontextmenu-invokecommand)：按菜单命令 ID 或动词执行命令。
- [IContextMenu3::HandleMenuMsg2](https://learn.microsoft.com/en-us/windows/win32/api/shobjidl_core/nf-shobjidl_core-icontextmenu3-handlemenumsg2)：转发原生菜单及扩展的消息。
