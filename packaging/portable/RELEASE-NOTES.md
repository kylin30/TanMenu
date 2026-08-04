## TanMenu 0.14.0

> 本版把界面整个换成 Windows 7 Aero，并把外壳改成 Windows 经典主菜单 + 选项对话框。0.10.0 到 0.13.0
> 这四个版本从未发布过（仓库里最新的标签仍然是 v0.9.6），所以从 0.9.x 升上来的用户会一次看到全部变化，
> 下面按「窗口 → 菜单栏 → 应用页 → 选项对话框 → 兼容性」的顺序写。

**先说三件事，它们是刻意的取舍，不是没做完。**

- **玻璃是「近似」，不是复刻。** Aero 的玻璃是一层实时模糊，加上一条色调带、一层内侧高光和一道高光扫
  过。Avalonia 只提供模糊（`AcrylicBlur`），所以色调带和内侧高光是我们自己画在模糊之上的，高光扫过没有
  做。标题栏之外的客户区是不透明的——Windows 7 本来也是这样：玻璃在窗框上，客户区是实色。
- **深色模式是时代错误，但保留。** Windows 7 只有「高对比度」这种无障碍模式，没有深色变体；深色配色是所
  用主题库作者自己的设计。保留它，是因为配色表两套齐全、而且用户已经习惯有这个选项。
- **剩下的图标仍然是 Segoe Fluent Icons。** Windows 7 根本没有图标字体，它的界面图标是 `shell32.dll` 里
  的全彩位图，不可再分发，所以没有可以换过去的字体。这是第二个明说的时代错误。不过用量已经很少：菜单栏
  按规范不用图标，设置行也不再有 16 DIP 的行图标，全部界面里只剩 7 处、6 个字形。

---

- 窗口标题栏改为手绘的 Aero 玻璃标题栏，高 30 DIP（原先是 48 DIP 的 Windows 11 标题条）：左边应用图标和
  标题，右边最小化 / 最大化 / 关闭三个按钮，宽度 28 / 27 / 48 DIP、高 19 DIP，全部量自微软自己发布的
  Windows 7 计算器截图。标题文字带 15 半径的外发光，15 是微软 DWM 文档里 `iGlowSize` 的原值。玻璃的色调
  取自系统个性化的主题色——Aero 的用户颜色本来就是 `DwmGetColorizationColor`，一个「窗框颜色」，这正是它
  原本的用途。窗口失去焦点时玻璃转为较淡的一档。拖动、双击最大化、右键系统菜单全部照旧，因为这条标题栏
  是真正的非客户区。
- **左侧导航窗格删除，改为 Windows 经典主菜单栏。** 标题栏正下方、客户区之内是一条 25 DIP 高的菜单栏：
  设置(O) · 打开主目录(M) · 刷新(R) · 退出(X) · 关于(A)。五项全是直接命令，没有下级菜单、没有图标、
  没有省略号——「菜单栏通常放菜单名，但也可以放命令项」是微软自己的原话，About / Options 这类「点了就开
  另一个窗口、不再需要用户补充信息」的项按规定不加省略号。每一项都有访问键（Alt 呼出）。
- 搜索框放在菜单栏里、紧跟在「关于」后面，中间隔一条竖直分隔条，宽 320 DIP、高 23 DIP。菜单栏在客户区
  之内，正好满足微软对窗框的原话规定：「不要往窗框里加控件，把控件放进窗口里」。右端的放大镜在有文字后
  变成「×」清除按钮（这也是 Windows 7 的原话规定）。`Ctrl+E` 聚焦搜索框并选中已有文字。
- 全部控件——按钮、复选框、列表、滚动条、进度条、文本框、数字微调框、下拉框和它的下拉行、分组框、
  菜单、选项卡——改用 Aero 外观。这些是从 MIT 授权的 aero-theme-for-avalonia 逐个文件**复制**进来的，
  不是作为依赖引用；每个副本都带着上游版权声明、来源版本和逐条改动记录，完整的 MIT 文本随可执行文件一
  起发布，文件名 `THIRD-PARTY-NOTICES.txt`。
- 配色、字号和间距整体换掉：正文从 14 DIP 改为 12 DIP（微软发布的「Segoe UI 9 pt 是系统字体」，96 dpi 下
  就是 12 DIP），字体从 Segoe UI Variable Text 改为普通 Segoe UI（Variable 的三档光学尺寸是 Windows 11
  的机制），间距换成微软发布的相对像素阶梯 3 / 5 / 7 / 9 / 11 / 16（再加一个写成 2×11 的 22），圆角从 4
  改为 3。分组标题用 Windows 7 发布的蓝色（浅色下 `#003399`）。界面上不再有任何一处自己指定字重——微软那
  张字体表给每种文字规定了字号和颜色，没有规定字重。
- 「应用」页的每个分类仍然各自装在一个面板里，但面板从「细边框 + 8 DIP 圆角 + 比页面深一档的底色」变成
  Aero 分组框：只有一条细边框、3 DIP 圆角、不填底色。分类之间的间距从 12 DIP 收到 11 DIP（发布值「无关
  控件之间 11」）。「应用」页底色改为资源管理器列表区的白。
- **「设置」不再是一个页面，而是一个模态的「选项」对话框**，也就是 Windows 7 的属性表：五个横向选项卡
  （外观 / 行为 / 文件夹 / 常用工具 / 更新），右下角一排 确定 / 取消 / 应用，按发布顺序右对齐、每个
  75 × 23。对话框不滚动——「属性页不要滚动，而是把页做大、把版面排紧」是原话规定——所以窗口按最高的那一
  页定尺寸（客户区 711 × 479 DIP），仍然远在发布的 800 × 600 下限之内。
- **改设置的方式变了：延迟提交。** 以前每改一项立刻生效并立刻写盘；现在改动先攒着，点 确定 或 应用 才
  写入，取消 则丢弃（有未提交的改动时会先问一句）。没有待提交的改动时 应用 是灰的，而且这个「脏」标记
  是拿草稿和当前配置逐项比出来的——改一项再改回去，应用 会重新变灰。「开机自启」也一样先攒着，取消 之
  后注册表不会被动过。只有三个会移动或覆盖真实数据的操作（更改 / 重置数据文件夹、从备份恢复）仍然是点了
  就问、问完就做：它们是命令，不是设置项。
- **设置行改成 Windows 7 的标签 + 控件栅格。** 左边一个标签、右边它的控件，间距走发布值（标签到控件 5、
  相关行之间 7、控件下方的说明 3、对话框边距 11）。Windows 11 的行卡片、行图标和副标题三样东西一起删
  除——属性页里没有这三个对象。四个「开关」和常用工具的总开关改成**复选框**（Windows 7 没有开关控件），
  标签直接写在复选框上。只有「文件夹」页保留分组框，因为只有它一页装了四组互不相干的东西（主文件夹 /
  数据文件夹 / 缓存 / 备份）；其余四页整页就是一组，选项卡标题本身就是组名。
- **「关于」成了真正的关于对话框**：模态、按内容定尺寸、右下角一个「确定」，里面是产品图标、发行者、
  当前版本和版权。它不再是设置里的一个分组，也不再占一个选项卡。
- 焦点框改成 Windows 经典的 1 px 点线矩形，替换 Windows 11 的双环焦点框；命令按钮按发布值统一到
  75 × 23 相对像素。
- 窗口最小宽度回到 711 DIP、首次打开的尺寸回到 1000×688——删掉导航窗格后，内容区旁边不再有东西要加宽度，
  这两个数就退回内容区自己的下限。窗口的定位规则不变：每次唤起和每次按内容重新适配后，都水平居中于主屏
  工作区、底边距工作区底部 8 DIP；手动拖走的窗口留在原处，直到内容变化或再次唤起。
- 配置、快捷方式缓存与图标缓存三个文件的磁盘格式完全没变，升级后无需重新扫描或重新提取图标；旧配置里
  已经废弃的字段仍然能正常载入，不会报错。延迟提交也不改磁盘格式：提交时只写它自己那份具名属性清单，
  别的键一个都不动。

---

> This release replaces the whole UI with Windows 7 Aero and reshapes the shell into a classic main
> menu plus an Options dialog. 0.10.0 through 0.13.0 were never released — the newest tag in this
> repository is still v0.9.6 — so anyone upgrading from 0.9.x sees all of it at once. The notes below
> run window → menu bar → Apps page → Options dialog → compatibility.

**Three things up front. They are deliberate trades, not unfinished work.**

- **The glass is an approximation, not a reproduction.** Aero's glass was a live blur plus a colour
  tint, a bright inner reflection and a specular sweep. Avalonia supplies the blur only
  (`AcrylicBlur`), so the tint and the reflection are drawn on top of it and the sweep is not
  reproduced. The client area below the caption is opaque — which is what Windows 7 did too: the glass
  was the window *frame* and the client under it was a solid colour.
- **Dark mode is an anachronism and is kept.** Windows 7 shipped High Contrast, an accessibility mode,
  not a dark variant; the dark palette is the theme library author's own invention. It stays because
  the palette has both variants and because users expect the choice.
- **The icons that remain are still Segoe Fluent Icons.** Windows 7 had no icon font at all — its UI
  icons were full-colour bitmaps inside `shell32.dll`, which are not redistributable — so there is
  nothing to switch the font to. That is the second stated anachronism. There is much less of it now:
  the menu bar takes no icons by rule and the settings rows lost their 16 DIP marks, leaving 7 glyph
  uses over 6 code points in the whole UI.

---

- The title bar is a hand-drawn Aero glass caption, 30 DIP tall, replacing the 48 DIP Windows 11 title
  strip: app icon and title on the left, minimize / maximize / close on the right at 28 / 27 / 48 DIP
  wide and 19 DIP tall, every figure measured off Microsoft's own published Windows 7 Calculator
  screenshot. The title carries a 15-radius glow — 15 is Microsoft's own `iGlowSize` from the DWM
  custom-frame documentation, not a number of ours. The glass is tinted with the user's Windows accent
  colour, which is exactly the job Aero's user colour had: `DwmGetColorizationColor` was a *frame*
  colour. The band drops to a fainter tint when the window loses focus. Dragging, double-click to
  maximize and the right-click system menu all work as before, because this caption really is
  non-client.
- **The left navigation pane is gone; a classic main menu bar replaces it.** Directly below the
  caption and inside the client area sits a 25 DIP band: Options · Open Main Folder · Refresh · Exit ·
  About. All five are direct commands — no submenus, no icons, no ellipses. Published, in as many
  words: *"A menu bar typically contains menu names, but it can also contain command items"*, and
  About/Options *"must display another window when clicked, but don't require additional information
  from the user. Therefore they don't need ellipses."* Every item has an access key (press Alt).
- Search sits in the menu bar, immediately after About with one vertical separator between them,
  320 DIP wide and 23 DIP tall. The band is inside the client area, which is what Microsoft's rule for
  a window frame asks for, verbatim: *"Don't add controls to a window frame. Put the controls within
  the window instead."* The magnifier at the right end becomes an "×" clear button once there is text
  — also published. `Ctrl+E` focuses the field and selects whatever is in it.
- Every control — buttons, check boxes, lists, scroll bars, progress bars, text boxes, the number
  spinner, combo boxes and their drop-down rows, group boxes, the menu and the tabs — is now Aero.
  Those visuals are **copied**, file by file, out of the MIT-licensed aero-theme-for-avalonia rather
  than taken as a package dependency; each copy carries the upstream copyright notice, the revision it
  came from and a numbered record of what was changed, and the full MIT text ships beside the
  executable as `THIRD-PARTY-NOTICES.txt`.
- Colour, type and spacing were replaced wholesale: body text 14 → 12 DIP (published — "Segoe UI 9 pt
  is the system font", and 9 pt at 96 dpi is 12 DIP), the face Segoe UI Variable Text → plain Segoe UI
  (Variable's three optical sizes are a Windows 11 mechanism), the spacing ladder 4/8/12/16/20 → the
  published relative-pixel rungs 3/5/7/9/11/16 plus one step stated as 2 × 11 = 22, and the control
  radius 4 → 3. Group headings take the blue Windows 7 published for them (`#003399` in light). No
  markup in the app states a font weight any more: Microsoft's own type table gives every text role a
  size and a colour and states a weight on none of them.
- Each category on the Apps page still sits in a panel of its own, but the panel changed from
  "hairline outline + 8 DIP corner + a fill one step off the page" to an Aero group box: outline only,
  3 DIP corner, no fill. The gap between categories tightened from 12 to 11 DIP (published, "between
  unrelated controls — 11"). The Apps page's own surface is Explorer's list-background white.
- **Settings is no longer a page. It is a modal Options dialog** — a Windows 7 property sheet with
  five horizontal tabs (Appearance / Behavior / Folders / Common Tools / Updates) and an OK / Cancel /
  Apply row, right-aligned in that published order, each button 75 × 23. Nothing scrolls: *"Don't
  scroll property pages. Instead of using scrollbars, make the property pages larger and lay out the
  pages efficiently."* So the dialog is sized to its tallest tab — 711 × 479 DIP of client — and still
  clears the published 800 × 600 floor with room.
- **How settings are applied changed: delayed commit.** Every control used to apply and persist the
  moment it changed. Edits now accumulate and are written by OK or Apply; Cancel discards them, asking
  first if there are any. Apply is disabled until something is genuinely pending, and the dirty flag is
  computed by comparing the draft against the live config — change a setting and change it back and
  Apply goes grey again. Autostart is buffered the same way, so a cancelled visit never touches the
  registry. The three actions that move or overwrite real data (change/reset the data folder, restore
  from backup) still confirm and then act immediately: they are commands, not properties.
- **The settings rows are a Windows 7 label-and-control grid.** A label in the left column, its control
  in the right, on the published ladder (5 label-to-control, 7 between related rows, 3 for a line under
  the control it describes, 11 for the dialog margin). The Windows 11 row card, the row icon and the
  subtitle line are all deleted — a property page has none of those objects. The four toggle switches
  and Common Tools' master switch are **check boxes** now, a control Windows 7 actually shipped, with
  their labels on the box itself. Only the Folders tab keeps group boxes, because it is the one tab
  holding four distinct sets (Main Folder / Data Folder / Cache / Backup); on the other four the tab
  header names the single set the page is.
- **About is a real About box**: modal, sized to its content, one OK button, showing the product tile,
  the publisher, the current version and the copyright. It is neither a settings group nor a tab any
  more.
- The focus rectangle is the classic Windows 1 px dotted rectangle, replacing Windows 11's two-ring
  focus ring, and command buttons are on the published 75 × 23 relative pixels.
- The window's minimum width is back to 711 DIP and its first-open size back to 1000×688. With the
  navigation pane deleted there is nothing beside the content to add width for, so both numbers fall
  back to the content area's own floor. Window placement is unchanged: on every summon and every re-fit
  the window is horizontally centred on the primary screen's work area with its bottom edge 8 DIP above
  it; a window you have dragged stays where you put it until its content changes or you summon it
  again.
- The on-disk formats of the config, shortcut cache and icon cache are unchanged, so upgrading needs
  no rescan and no icon re-extraction, and fields retired from older configs still deserialize without
  error. Delayed commit does not change that either: a commit writes only its own named list of
  properties and touches no other key.
