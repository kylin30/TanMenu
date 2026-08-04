## TanMenu 0.13.0

> 本版把界面整个换成 Windows 7 Aero。0.10.0 / 0.11.0 / 0.12.0 三个版本从未发布过（仓库里最新的标签是
> v0.9.6），所以从 0.9.x 升上来的用户会一次看到全部变化，下面按「窗口 → 应用页 → 设置页 → 兼容性」的
> 顺序写。

**先说三件事，它们是刻意的取舍，不是没做完。**

- **玻璃是「近似」，不是复刻。** Aero 的玻璃是一层实时模糊，加上一条色调带、一层内侧高光和一道高光扫
  过。Avalonia 只提供模糊（`AcrylicBlur`），所以色调带和内侧高光是我们自己画在模糊之上的，高光扫过没有
  做。标题栏之外的客户区是不透明的——Windows 7 本来也是这样：玻璃在窗框上，客户区是实色。
- **深色模式是时代错误，但保留。** Windows 7 只有「高对比度」这种无障碍模式，没有深色变体；深色配色是所
  用主题库作者自己的设计。保留它，是因为配色表两套齐全、而且用户已经习惯有这个选项。
- **图标仍然是 Segoe Fluent Icons。** Windows 7 根本没有图标字体，它的界面图标是 `shell32.dll` 里的全彩
  位图，不可再分发，所以没有可以换过去的字体。这是第二个明说的时代错误。

---

- 窗口标题栏改为手绘的 Aero 玻璃标题栏，高 30 DIP（原先是 48 DIP 的 Windows 11 标题条）：左边应用图标和
  标题，右边最小化 / 最大化 / 关闭三个按钮，宽度 28 / 27 / 48 DIP、高 19 DIP，全部量自微软自己发布的
  Windows 7 计算器截图。标题文字带 15 半径的外发光，15 是微软 DWM 文档里 `iGlowSize` 的原值。玻璃的色调
  取自系统个性化的主题色——Aero 的用户颜色本来就是 `DwmGetColorizationColor`，一个「窗框颜色」，这正是它
  原本的用途。窗口失去焦点时玻璃转为较淡的一档。拖动、双击最大化、右键系统菜单全部照旧，因为这条标题栏
  是真正的非客户区。
- 搜索框从标题栏移回「应用」页顶部，宽 320 DIP、高 23 DIP。微软对窗框的规定是原话：「不要往窗框里加控
  件，把控件放进窗口里」。右端的放大镜在有文字后变成「×」清除按钮（这也是 Windows 7 的原话规定：即时搜
  索模式下，用户开始输入后出现清除按钮，「×」替换放大镜）。新增 `Ctrl+E` 聚焦搜索框并选中已有文字。原先
  的强调色下划线聚焦样式取消了——Windows 7 的文本框靠重绘自身来表示聚焦，不画下划线。
- 左侧导航从 48 DIP 的纯图标栏改为 160 DIP 的带文字导航窗格：每一行是一个 16 DIP 图标加一行文字。因为文
  字直接可见，原先每行的工具提示全部删除——微软术语表对工具提示的定义是「给所指向的**未标注**控件加标签
  的小弹出窗口」，重复一遍已经看得见的文字就是用错了控件。行的顺序不变：应用 在上，下方固定 打开主目录 ·
  刷新 · 设置 · 分隔线 · 退出。选中行的样式换成 Aero 列表行自己的两层描边加渐变，原先那个按任务管理器量
  出来的 16 DIP 强调色胶囊随图标栏一起删除。
- 全部控件——按钮、复选框、开关、列表、滚动条、进度条、文本框、数字微调框、下拉框和它的下拉行、分组框
  ——改用 Aero 外观。这些是从 MIT 授权的 aero-theme-for-avalonia 逐个文件**复制**进来的，不是作为依赖引
  用；每个副本都带着上游版权声明、来源版本和逐条改动记录，完整的 MIT 文本随可执行文件一起发布，文件名
  `THIRD-PARTY-NOTICES.txt`。
- 配色、字号和间距整体换掉：正文从 14 DIP 改为 12 DIP（微软发布的「Segoe UI 9 pt 是系统字体」，96 dpi 下
  就是 12 DIP），字体从 Segoe UI Variable Text 改为普通 Segoe UI（Variable 的三档光学尺寸是 Windows 11
  的机制），间距阶梯从 4/8/12/16/20 换成 Aero 发布的 5/7/11（再加一个写成 2×11 的 22），圆角从 4 改为 3。
  分组标题用 Windows 7 发布的蓝色（浅色下 `#003399`）。
- 「应用」页的每个分类仍然各自装在一个面板里，但面板从「细边框 + 8 DIP 圆角 + 比页面深一档的底色」变成
  Aero 分组框：只有一条细边框、3 DIP 圆角、不填底色。分类之间的间距从 12 DIP 收到 11 DIP（发布值「无关
  控件之间 11」）。「应用」页底色改为资源管理器列表区的白，其余页面仍然不画任何底，直接露出窗口自己的
  实色客户区。
- 「设置」页改成 Windows 7 属性页的样子：一列可滚动的分组框，每个分组的标题嵌在自己边框上方的缺口里，
  外观 / 行为 / 文件夹 / 常用工具 / 更新 / 关于 六组。没有「确定 / 取消 / 应用」按钮，因为每一项仍然是改
  完立即生效并立即写盘。原先每一行外面那张 Windows 11 卡片（半透明底 + 细边框 + 4 DIP 圆角）删除了，行
  本身的排布规范一点没动：左边 16 DIP 图标，中间一行标题加一行说明，控件靠右对齐在同一条线上，附加内容
  另起一行从标题左边缘开始，十六行全部照此。
- 焦点框改成 Windows 经典的 1 px 点线矩形，替换 Windows 11 的双环焦点框。
- 窗口最小宽度从 760 DIP 改为 872 DIP，首次打开的尺寸从 1000×688 改为 1112×688。这两个数都只是跟着导航
  窗格宽了 112 DIP 平移：内容区自己的下限仍然是原来的 711 DIP，没有变窄。
- 窗口的定位规则不变：每次唤起和每次按内容重新适配后，都水平居中于主屏工作区、底边距工作区底部 8 DIP；
  手动拖走的窗口留在原处，直到内容变化或再次唤起。
- 配置、快捷方式缓存与图标缓存三个文件的磁盘格式完全没变，升级后无需重新扫描或重新提取图标；旧配置里
  已经废弃的字段仍然能正常载入，不会报错。

---

> This release replaces the whole UI with Windows 7 Aero. 0.10.0, 0.11.0 and 0.12.0 were never
> released — the newest tag in this repository is v0.9.6 — so anyone upgrading from 0.9.x sees all of
> it at once. The notes below run window → Apps page → Settings page → compatibility.

**Three things up front. They are deliberate trades, not unfinished work.**

- **The glass is an approximation, not a reproduction.** Aero's glass was a live blur plus a colour
  tint bar, a bright inner reflection and a specular sweep. Avalonia supplies the blur only
  (`AcrylicBlur`), so the tint and the reflection are drawn on top of it and the sweep is not
  reproduced. The client area below the caption is opaque — which is what Windows 7 did too: the glass
  was the window *frame* and the client under it was a solid colour.
- **Dark mode is an anachronism and is kept.** Windows 7 shipped High Contrast, an accessibility mode,
  not a dark variant; the dark palette is the theme library author's own invention. It stays because
  the palette has both variants and because users expect the choice.
- **The icons are still Segoe Fluent Icons.** Windows 7 had no icon font at all — its UI icons were
  full-colour bitmaps inside `shell32.dll`, which are not redistributable — so there is nothing to
  switch the font to. That is the second stated anachronism.

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
- Search moved out of the title bar and back to the top of the Apps page, 320 DIP wide and 23 DIP
  tall. Microsoft's rule for a window frame is verbatim: *"Don't add controls to a window frame. Put
  the controls within the window instead."* The magnifier at the right end becomes an "×" clear button
  once there is text — also published: in instant-search mode a clear button appears after the user
  starts typing, and an "×" replaces the magnifying glass. `Ctrl+E` is new: it focuses the field and
  selects whatever is in it. The accent underline that used to mark focus is gone; a Windows 7 text
  box signals focus by repainting itself, not by drawing a bar under it.
- The 48 DIP icon-only navigation rail became a 160 DIP **labelled** navigation pane: each row is a
  16 DIP glyph plus a visible label. Because the labels are back, every row's tooltip was deleted —
  Microsoft's glossary defines a tooltip as a small pop-up that labels the **unlabeled** control being
  pointed to, so a tooltip repeating a visible label is the wrong control. The order is unchanged:
  Apps at the top, then a bottom-pinned Open home folder · Refresh · Settings · separator · Exit. The
  selected row is now Aero's own two nested borders over a gradient; the 16 DIP accent pill that was
  measured against Task Manager went with the icon rail that framed it.
- Every control — buttons, checkboxes, toggles, lists, scroll bars, progress bars, text boxes, the
  number spinner, combo boxes and their drop-down rows, and group boxes — is now Aero. Those visuals
  are **copied**, file by file, out of the MIT-licensed aero-theme-for-avalonia rather than taken as a
  package dependency; each copy carries the upstream copyright notice, the revision it came from and a
  numbered record of what was changed, and the full MIT text ships beside the executable as
  `THIRD-PARTY-NOTICES.txt`.
- Colour, type and spacing were replaced wholesale: body text 14 → 12 DIP (published — "Segoe UI 9 pt
  is the system font", and 9 pt at 96 dpi is 12 DIP), the face Segoe UI Variable Text → plain Segoe UI
  (Variable's three optical sizes are a Windows 11 mechanism), the spacing ladder 4/8/12/16/20 → Aero's
  published 5/7/11 plus one step stated as 2 × 11 = 22, and the control radius 4 → 3. Group headings
  take the blue Windows 7 published for them (`#003399` in light).
- Each category on the Apps page still sits in a panel of its own, but the panel changed from
  "hairline outline + 8 DIP corner + a fill one step off the page" to an Aero group box: outline only,
  3 DIP corner, no fill. The gap between categories tightened from 12 to 11 DIP (published, "between
  unrelated controls — 11"). The Apps page's own surface is Explorer's list-background white; every
  other page still paints nothing at all and shows the window's own opaque client surface.
- Settings is now shaped like a Windows 7 property page: one scrolling column of group boxes, each
  with its label notched into the top of its own outline — Appearance / Behavior / Folders / Common
  Tools / Updates / About. There is no OK/Cancel/Apply, because every control still applies and
  persists the moment it changes. The Windows 11 row card around each row (translucent fill, hairline,
  4 DIP corner) is gone, and the row layout inside is untouched: a 16 DIP icon on the left, one title
  line and one subtitle line, the control trailing on one shared right edge, extra content on a row of
  its own starting at the title's left edge — all sixteen rows.
- The focus rectangle is the classic Windows 1 px dotted rectangle, replacing Windows 11's two-ring
  focus ring.
- The window's minimum width moved from 760 to 872 DIP and its first-open size from 1000×688 to
  1112×688. Both are the navigation pane's own 112 DIP of growth passed straight through: the content
  area's floor is still the 711 DIP it always was, so nothing got narrower.
- Window placement is unchanged: on every summon and every re-fit the window is horizontally centred
  on the primary screen's work area with its bottom edge 8 DIP above it; a window you have dragged
  stays where you put it until its content changes or you summon it again.
- The on-disk formats of the config, shortcut cache and icon cache are unchanged, so upgrading needs
  no rescan and no icon re-extraction, and fields retired from older configs still deserialize without
  error.
