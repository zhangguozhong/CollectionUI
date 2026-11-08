# CollectionUI 使用说明

CollectionUI 用于在 Unity UGUI 中显示分组列表、瀑布流和自动换行标签。适用于 Unity 2022.3 / UGUI 1.0，支持普通节点展开和 ScrollRect 滚动。

本文只说明如何使用。内部实现见 [设计文档](DESIGN.md)。示例中的业务模型、尺寸提供者及请求处理方法由使用方实现。

## 目录

- [1. 导入与演示](#1-导入与演示)
- [2. 创建集合视图](#2-创建集合视图)
- [3. 数据与尺寸](#3-数据与尺寸)
- [4. 常用布局](#4-常用布局)
- [5. Cell 绑定与复用](#5-cell-绑定与复用)
- [6. 更新数据与定位](#6-更新数据与定位)
- [7. 刷新加载与预加载](#7-刷新加载与预加载)
- [8. Inspector 参数](#8-inspector-参数)
- [9. 使用注意与常见问题](#9-使用注意与常见问题)

## 1. 导入与演示

导入 `CollectionUI.unitypackage`，或完整复制 `Assets/CollectionUI` 及其 `.meta` 文件。

仅使用运行时可以不导入示例、编辑器工具和测试；保留编辑器工具时应同时保留其引用的示例程序集。使用 asmdef 的业务程序集需要引用 `CollectionUI`。

三个演示场景均位于 [Samples/Generated](Samples/Generated)：

| 场景 | 验证内容 |
|---|---|
| `CollectionViewDemo.unity` | 1,000/10,000 条数据、自动滚动、等宽列、任意矩形、标签、内容测量、LTR/RTL 切换 |
| `ExpandedCollectionDemo.unity` | 普通节点展开、标签换行、可配置高度自适应及后续区域布局 |
| `InteractionDemo.unity` | 手动完成刷新/加载、预加载互斥、无更多数据、吸顶、定位及差量更新 |

交互演示中点击 `COMPLETE` 模拟数据返回；关闭 `PRELOAD` 后可单独体验上拉 loading。万条数据性能在第一个场景验证。示例界面保留英文标签和 Unity 内置字体，以便直接导入运行；示例图片是色块，不访问网络。

主演示页头的 `LTR / LEFT START` 按钮可切换为 `RTL / RIGHT START`，四种场景及 1,000/10,000 条数据均可使用。标签和商品序号用于观察首项在右侧、同一行从右向左排列；示例文字保持原样，不演示阿拉伯文字体或连字。

菜单 `Tools → Collection UI` 可打开或生成演示。生成前请保存当前场景。

## 2. 创建集合视图

### 2.1 ScrollRect 滚动模式

先使用 Unity Scroll View 或自行建立以下层级：

```text
Canvas / 安全区父区域
└─ Collection（ScrollRect、CollectionView）
   └─ Viewport（RectTransform、RectMask2D、可接收射线的 Image）
      └─ Content（RectTransform）
```

1. 将 `CollectionView` 与 `ScrollRect` 放在同一节点。
2. 设置 `ScrollRect.viewport` 和 `ScrollRect.content`，Content 作为 Viewport 直接子节点。
3. 设置模板，或调用 `RegisterTemplate`。
4. 等父区域具有有效宽度后调用 `ApplySnapshot`。
5. 使用刷新/加载时设置 `ScrollRect.movementType = Elastic`，并保证 Canvas 的射线检测和 EventSystem 可用。

**组件不会自动创建 ScrollRect、Viewport 或 Content。** 首次使用时才初始化，因此可先在活动节点添加组件，再配置引用。读取 `collection.ScrollRect` 也会触发初始化。

Content 高度及 Cell 外框由集合管理。不要在 Content 上添加竞争布局的 `LayoutGroup` 或 `ContentSizeFitter`，也不要由其他布局同时控制 Cell 外框；Cell 内部可以使用布局组件。

### 2.2 普通节点完整展开

在 Canvas 下有 `RectTransform` 的普通 UI 节点添加组件即可，无需 ScrollRect、Viewport 或额外 Content。

```csharp
var collection = panel.gameObject.AddComponent<CollectionView>();
collection.DisplayMode = CollectionDisplayMode.Expanded;
collection.AutoHeight = true;
collection.RegisterTemplate("item", cellPrefab);
collection.ApplySnapshot(snapshot); // panel 此时必须具有正数宽度
```

| 模式 | 内容容器 | Cell 创建范围 |
|---|---|---|
| `Auto`，默认 | 本节点有 ScrollRect 则采用其 Content，否则采用自身 | 首次初始化时确定 |
| `Scroll` | 本节点 ScrollRect 的 Content | 视区及 Overscan 范围 |
| `Expanded` | 自身 RectTransform | 全部条目、页眉和页脚 |

`DisplayMode` 请在首次使用前设置，初始化后不可切换。Auto 模式下存在 ScrollRect 时，必须配置完整引用。使用 Expanded 时请禁用或移除不用的 ScrollRect。

`AutoHeight` 只影响 Expanded：开启后高度跟随内容，空内容可以缩至零；关闭后保留外部指定高度，不自动裁剪。组件保留宿主宽度、锚点、轴心和位置；希望顶边固定时应设置顶部轴心。高度自适应时，不要让父布局或 ContentSizeFitter 同时控制该高度。

大量数据请使用 Scroll。Expanded 不支持滚动定位，也不会自动裁剪内容。Scroll 的 Content 高度始终跟随布局，与 AutoHeight 无关。

## 3. 数据与尺寸

### 3.1 最小数据示例

以下示例假定 `collection` 已配置，`cellPrefab` 挂有 `CollectionCell` 或其子类。

```csharp
using CollectionUI;

collection.RegisterTemplate("item", cellPrefab);

var snapshot = new CollectionSnapshot();
var section = new CollectionSection
{
    Id = "section-a",
    Layout = new SectionLayoutSettings
    {
        Mode = LayoutMode.Columns,
        ColumnCount = 2,
        Insets = new Insets(16, 16, 16, 16), // 左、上、右、下
        HorizontalSpacing = 12,
        VerticalSpacing = 16,
        SectionSpacing = 24
    }
};
section.Items.Add(new CollectionItem
{
    Id = "item-1",
    ReuseIdentifier = "item",
    Version = 1,
    Size = ItemSize.Fixed(0, 180), // 宽度 0 表示使用可用列宽
    Data = businessModel
});
snapshot.Sections.Add(section);
collection.ApplySnapshot(snapshot);
```

`CollectionSection.Header` 和 `Footer` 也使用 `CollectionItem`，可以选择不同模板。模板必须先注册；同一模板 ID 不能替换为不同 Prefab，要更换资源请使用新 ID。

### 3.2 身份与有效值

- 分区 ID 在快照内唯一；普通条目 ID 在整个集合内唯一，不能只在组内唯一。
- 页眉/页脚的身份包含分区，ID 可以跨分区重复。
- ID、模板 ID 非空；尺寸必须有限且为正，间距和内边距有限且非负。
- 宽度不能超出布局允许范围；组件不自动缩放超宽条目。
- 已发布布局中的元数据不可修改；通过 `GetSnapshotCopy()` 获取可修改副本。
- `Data` 是业务对象引用，不会深复制，其内容变化也不会被自动观察。

### 3.3 按业务内容计算尺寸

使用 `ItemSize.Measured()` 和 `MeasureItem`，按内容与可用宽度同步返回尺寸，无需先创建 Cell。

```csharp
collection.MeasureItem = (item, availableWidth) =>
{
    var data = (MyItemData)item.Data;
    return businessSizeProvider.Measure(data, availableWidth);
};
// 需要测量的条目使用此尺寸模式。
item.Size = ItemSize.Measured();
```

这里的 `MyItemData` 和 `businessSizeProvider` 由业务实现；示例展示接入契约。

| 尺寸描述 | 含义 |
|---|---|
| `ItemSize.Fixed(width, height)` | 固定尺寸；width 为 0 时采用可用宽度 |
| `ItemSize.Aspect(width, widthOverHeight)` | 采用宽度与宽高比计算高度 |
| `ItemSize.Measured(width = 0)` | 传入解析后的可用宽度，由 `MeasureItem` 返回宽高 |

测量规则、字号、字体、内边距应与 Cell 展示一致。图片尺寸优先使用接口返回的宽高比。确实无法提前知道尺寸时，可以先使用估算值，结果到达后调用 `InvalidateItemSize(id)`；组件会尽量保持锚点，但尺寸改变本身可能引起重排，不等同于绝对不移动。

## 4. 常用布局

### 4.1 标签：等高、宽度随内容、自动换行

```csharp
var textMeasurer = new UguiTextMeasurer(
    font, 28, pixelsPerUnit: canvas.scaleFactor);
var tagMeasurer = new SingleLineMeasurer(
    textMeasurer, height: 96, horizontalPadding: 20, minWidth: 96);

section.Layout.Mode = LayoutMode.Flow;
section.Layout.HorizontalSpacing = 16;
section.Layout.VerticalSpacing = 20;
collection.MeasureItem = (item, width) =>
    tagMeasurer.Measure((string)item.Data, width).Size;
// 每个标签：Size = ItemSize.Measured()，Data = 标签文字。
```

Flow 严格保持输入顺序；恰好放下不换行，否则进入下一行。行高取该行最大高度，条目顶部对齐，不回填前面空隙。`SingleLineMeasurer` 负责固定高度、水平内边距和超长文字省略；换行符会转为空格。

Cell 绑定时使用同一个测量配置，并采用 `ContentMetrics.DisplayText`，避免测量已省略而显示仍使用原文。完整示例见 [TagCell.cs](Samples/TagCell.cs) 与 [DemoContent.cs](Samples/DemoContent.cs)。

### 4.2 商品卡片：等宽列、内容决定高度

设置 `Mode = LayoutMode.Columns`、`ColumnCount` 和 `ItemSize.Measured()`。可直接使用业务测量器，或组合通用内容元素：

```csharp
var elements = new[]
{
    ContentElement.Aspect(imageWidthOverHeight),
    ContentElement.Text(textMeasurer, title),
    ContentElement.Text(textMeasurer, subtitle)
};
LayoutMetrics metrics = VerticalContentMeasurer.Measure(
    elements, availableWidth, new Insets(16, 16, 16, 16), spacing: 12);
// 测量回调返回 metrics.Size；绑定时用 metrics.ElementRects 排列内部元素。
```

业务可以自行选择内容元素。`ContentElement.Fixed` 支持固定元素，宽度 0 填满内部可用宽度。空元素序列可能返回零高，不应作为有效 Cell 提交。

完整示例见 [ProductCell.cs](Samples/ProductCell.cs)。测量结果包含矩形数组，建议按内容版本、宽度和样式缓存。字体或 Canvas 缩放改变后，重建匹配的测量器并调用 `InvalidateStyle()`。

### 4.3 任意矩形与分区间距

任意宽高使用 `LayoutMode.Skyline`，为条目指定 `ItemSize.Fixed(width, height)` 或业务测量尺寸。宽度必须在分区可用范围内，条目不按整齐的行排列。

Columns 的宽度 0 填满列，较窄的显式宽度按排列起点对齐（LTR 靠左、RTL 靠右）；不支持跨列。页眉/页脚宽度 0 填满分区内部宽度。`VerticalSpacing` 也用于页眉、主体、页脚之间；`SectionSpacing` 属于前一可见分区，最后一组后不追加组间距。空分区默认隐藏，`ShowEmptySection=true` 时保留其页眉、页脚及内边距。

### 4.4 从右向左排列（RTL）

每个分区独立设置方向，默认 `LeftToRight`。阿拉伯语等场景可由业务根据语言设置为 `RightToLeft`：

```csharp
var snapshot = collection.GetSnapshotCopy();
snapshot.Sections[0].Layout.Direction = LayoutDirection.RightToLeft;
collection.ApplySnapshot(snapshot);
```

初次创建数据时也可以直接设置 `section.Layout.Direction`。整页切换时遍历所有分区设置后，一次性提交快照；不同分区可以混用 LTR 和 RTL。

- Flow 从右侧开始向左排，换行后重新从右侧开始。
- Columns 优先最短列；同高时优先右侧列，窄 Cell 靠列右侧。
- Skyline 优先最低可放置位置；同高时优先右侧。
- 非满宽页眉、页脚及吸顶页眉靠右；左右 Insets 仍指物理左边和右边，不自动交换。

数据顺序、ID、尺寸测量方式及竖直滚动方向保持不变。运行时切换通过新快照或 `UpdateSection` 提交，组件会更新横向位置并尽量保持竖直滚动锚点。仅切换方向建议提交快照以复用未变的尺寸；`UpdateSection` 按原有契约强制重测和重绑整组。若语言切换同时改变文案或字体，还需更新条目 `Version` 或调用相应的尺寸/样式失效 API。自定义 `ICollectionLayout` 需要自行处理 `Direction`。Cell 内的文字方向、阿拉伯语连字、图标和内部排版由业务自己的文本/UI 组件处理。

## 5. Cell 绑定与复用

继承 `CollectionCell`，将业务显示放进生命周期方法：

```csharp
protected override void OnBind(LayoutEntry entry)
{
    // 读取 entry.Item.Data，设置文字、图片占位和交互状态。
}

protected override void OnLayoutUpdated(LayoutEntry entry)
{
    // 数据绑定未变但布局重新提交；更新内部尺寸或依赖索引的显示。
}

protected override void OnUnbind()
{
    // 移除监听、释放资源句柄，并清理本次绑定状态。
}
```

内容变化影响尺寸时更新 `CollectionItem.Version`；异步结果回填使用 `BindingVersion` 校验。

发起异步资源请求前捕获 `ItemId`、`BindingVersion` 和 `BindingCancellation`；结果回到 Unity 主线程后，仅在 `IsCurrent(capturedId, capturedVersion)` 为 true 时应用。解绑会使旧绑定失效并取消已创建的令牌。图片缓存、占位和资源释放仍由业务负责。

在 `OnLayoutUpdated` 中更新依赖外框或索引的显示。启用吸顶时，同一页眉可能对应多个 Cell 实例。重写 `OnDestroy` 时调用基类实现。

## 6. 更新数据与定位

| API | 使用时机 | 是否重新布局 |
|---|---|---|
| `ApplySnapshot(snapshot)` | 首次加载或替换完整数据 | 是，允许复用未变分区几何 |
| `ApplyChanges(changes)` | 批量插入、删除、移动、显式更新 | 是 |
| `ReloadItems(ids)` | 共享 Data 的显示变化，尺寸不变 | 否，只重绑 |
| `InvalidateItemSize(id)` | 普通条目的尺寸规则或内容改变 | 下一次 LateUpdate 合并处理 |
| `InvalidateStyle()` | 字体、缩放、全局测量规则改变 | 下一次更新重建 |
| `RefreshVisible()` | 需要同步视区显示 | 不重建几何 |

```csharp
collection.ApplyChanges(new CollectionChangeSet()
    .InsertItem("section-a", 0, newItem)
    .MoveItem("existing-id", "section-b", 2)
    .UpdateItem("changed-id", replacement)
    .RemoveItem("removed-id"));
```

支持对应的分区增删改移。索引按每一步操作后的数据解释，Move 的目标索引按移除源项后计算；更新对象必须保留原 ID。

显式 `UpdateItem`、`UpdateSection` 强制更新目标的测量与绑定，不要求递增 Version。普通 `ApplySnapshot` 不会检查 Data 对象内部，内容影响尺寸时请递增 Version 或使用失效 API。`ReloadItems` 不会测量新高度。更换 `MeasureItem` 后需调用 `InvalidateStyle()`。

### 吸顶与定位

在 CollectionView 同节点添加 `CollectionStickyHeaders` 即可使用页眉模板吸顶；下一分区将前一页眉推出，无页眉分区清除前一吸顶头。Expanded 不启用吸顶。

```csharp
collection.ScrollToItem("item-1", alignment: 0.5f); // 0 顶部，0.5 居中，1 底部
collection.ScrollToSection("section-b", animated: true, duration: 0.25f);
```

分组定位优先页眉，没有页眉则取第一条目；隐藏、空或不存在的目标不操作。分组动画使用非缩放时间，拖动、重新布局、禁用或新定位会取消旧动画。加载期间定位只解除位置保持，不结束业务请求。

### 内容尺寸通知

订阅 `ContentSizeChanged(Vector2 size)` 可安排后续区域。首次成功布局会通知，之后仅总宽/高改变时通知；Scroll 和 Expanded 都支持。普通滚动、同尺寸重绑、仅切换 AutoHeight 不触发。

回调时几何已提交，可以读取 Layout 和调整外层布局；不要在回调内同步向当前集合提交数据或使尺寸失效。解绑事件订阅，并将需要再次更新集合的操作延后执行。

## 7. 刷新加载与预加载

### 7.1 配置与请求时序

在已配置 ScrollRect 的 CollectionView 同节点添加 `CollectionLoader`，滚动方式使用 Elastic。业务传入根节点为 RectTransform 的 **Prefab 资源**，不需要提前实例化：

```csharp
loader.Configure(refreshLoadingPrefab, loadMoreLoadingPrefab);
loader.PreloadEnabled = true;
loader.PreloadDistance = 300;
loader.RefreshRequested += OnRefreshRequested;
loader.LoadMoreRequested += OnLoadMoreRequested;
loader.PreloadRequested += OnPreloadRequested;
// 三个处理方法均接收 long requestId，由业务发起网络请求。
```

组件在 Viewport 下创建独立实例；两个方向可以使用同一个 Prefab。完成后隐藏复用，禁用时保留，更换配置或销毁组件时清理。实例不阻挡拖动。根节点上的脚本可实现 `ICollectionLoadingView.SetLoading(bool)`，也可用 OnEnable/OnDisable 播放动画。

请求返回后，在 Unity 主线程执行：

```csharp
if (loader.ActiveRequestId != requestId)
    return; // 先过滤旧响应，防止其覆盖当前数据

collection.ApplyChanges(changes);
loader.CompleteLoadMore(requestId, hasMore: responseHasMore);
// 预加载使用 CompletePreload；刷新提交完整数据后使用 CompleteRefresh。
```

顺序是：**请求事件 → 业务获取数据 → 校验 requestId → 提交数据 → Complete**。提交数据不会自动关闭 loading；没有超时自动结束。完成后由 Loader 平滑回弹，不要再立即定位首组或手动把偏移归零，否则会打断回弹。失败由业务提示，再调用对应 Complete 释放请求锁。错误类型或过期 ID 的 Complete 返回 false，不会关闭新请求。

刷新后是否恢复分页由业务决定，在 `CompleteRefresh` 前设置 `loader.HasMore`。`CompleteLoadMore` / `CompletePreload` 的 hasMore 默认值为 true，建议始终显式传入实际结果。

`BeginRefresh()` / `BeginLoadMore()` 可主动发起请求；返回请求 ID，忙碌、禁用或不满足分页条件时返回 0。没有公开的主动预加载入口。请求回调可同步完成；不要假设 Begin 返回时请求仍在进行。

### 7.2 触发规则

| 交互 | 触发时机 | loading |
|---|---|---|
| 下拉刷新 | 顶部越界达到 TriggerDistance 后松手 | 顶部显示至 CompleteRefresh |
| 上拉加载 | 底部越界达到 TriggerDistance 后松手，且 HasMore=true | 底部显示至 CompleteLoadMore |
| 预加载 | 有效上拉手势中，距底部不超过 PreloadDistance，且 HasMore=true | 不显示 |

预加载同时受两层约束：

1. 刷新、加载更多、预加载共用请求锁，同时只能有一个未完成请求。
2. 同一手势最多预加载一次；即使该请求在松手前完成，也不再次触发，松手时也不追加一次加载更多。

惯性、程序定位、手指向下移动及停在底部不触发预加载。新手势开始时若旧请求未完成，旧请求完成后，新手势未用过的预加载机会仍可触发，但仍需满足方向和距离条件。

`HasMore` 是业务维护的非序列化状态，默认 true，不在 Inspector 显示。false 禁止上拉加载和预加载，不影响刷新，也不会主动结束正在进行的请求。功能开关负责是否启用能力，HasMore 负责当前数据是否还有下一页。

不提供拖动中、可松手和失败的展示状态。用户可在 loading 期间继续浏览；新手势/定位只解除位置保持。禁用 Loader 会取消其请求状态，网络任务的取消和旧响应过滤由业务完成。

## 8. Inspector 参数

鼠标停在字段名称上可查看中文 Tooltip。自定义 Inspector 还会检查模板、层级及布局所有权冲突，不会自动修改场景。

### CollectionView

| 参数 | 默认值 | 作用 |
|---|---|---|
| Display Mode | Auto | 首次初始化时选择滚动或展开 |
| Auto Height | true | 仅控制 Expanded 宿主高度自适应 |
| Templates | 空 | 模板 ID 与 Cell Prefab 映射；也可用代码注册 |
| Overscan | 240 | 视口上下各额外准备的 UI 距离，不是数量 |
| Max Pooled Per Template | 64 | 每种模板闲置实例上限，不限制活动 Cell |

### CollectionLoader

| 参数 | 默认值 | 作用 |
|---|---|---|
| Refresh Loading Prefab | 空 | 业务顶部 loading 资源 |
| Load More Loading Prefab | 空 | 业务底部 loading 资源 |
| Refresh Enabled | true | 启用下拉刷新 |
| Load More Enabled | true | 启用上拉加载 |
| Preload Enabled | false | 启用手势预加载 |
| Trigger Distance | 96 | 松手分发请求所需的越界距离 |
| Preload Distance | 300 | 距底部进入预加载范围的距离 |
| Return Duration | 0.18 | loading 保持与回弹的平滑时间参数，不是精确总时长或请求超时 |

只有预加载时，先关闭 RefreshEnabled、LoadMoreEnabled，再调用 `Configure(null, null)`。吸顶组件的 `Sticky Enabled` 默认 true。

## 9. 使用注意与常见问题

- 所有 API 在 Unity 主线程调用；不要在测量、绑定或尺寸事件回调中同步再次提交当前集合的数据。
- 保持条目 ID 稳定，将同一批数据合并提交。
- 大量数据使用 Scroll；普通节点展开适合较少条目。
- 所有尺寸和间距使用 Canvas UI 单位，不是屏幕像素。
- 内置文字测量器适用于动态 Font 与 UGUI Text；使用 TMP 时自行实现 MeasureItem。
- 当前支持竖向滚动，不支持横向列表和跨列条目。

常见问题：

| 现象 | 优先检查 |
|---|---|
| 初始化报错 | ScrollRect 引用、模板注册、父区域宽度 |
| 标签宽度或卡片高度不一致 | 测量字体/缩放/内边距是否与显示一致 |
| 文本改变但高度没更新 | Version、UpdateItem、InvalidateItemSize 的使用 |
| Cell 显示旧图片 | 异步结果是否检查 ItemId 与 BindingVersion |
| 有 loading 但一直不结束 | 是否收到业务结果并调用匹配的 Complete |
| 预加载不触发 | 手势方向、底部距离、HasMore、请求锁和本手势配额 |
| 普通节点内容超出边界 | AutoHeight=false 不裁剪，需要父遮罩或 Scroll 模式 |
