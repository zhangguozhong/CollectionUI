using UnityEngine;
using UnityEngine.UI;

namespace CollectionUI.Samples
{
    /// <summary>独立交互示例：业务请求由按钮显式完成，不使用计时器或网络模拟自动结束。</summary>
    public sealed class CollectionInteractionDemo : MonoBehaviour
    {
        public const int CurrentSchema = 2;
        [SerializeField]
        int schema = CurrentSchema;
        [SerializeField]
        CollectionView collection;
        [SerializeField]
        CollectionLoader interaction;
        [SerializeField]
        Text statistics;
        [SerializeField]
        Button refresh, loadMore, preload, complete, noMore, nextSection, insert, move, remove;
        bool initialized;
        int serial = 30, sectionIndex, refreshRequests, loadRequests, preloadRequests;
        public int Schema => schema;
        public CollectionView Collection => collection;
        public CollectionLoader Interaction => interaction;

        public void Configure(CollectionView view, CollectionLoader controller, Text stats, Button[] controls)
        {
            collection = view;
            interaction = controller;
            statistics = stats;
            refresh = controls[0];
            loadMore = controls[1];
            preload = controls[2];
            complete = controls[3];
            noMore = controls[4];
            nextSection = controls[5];
            insert = controls[6];
            move = controls[7];
            remove = controls[8];
        }

        void Start() => Initialize();
        /// <summary>运行和编辑器截图共用的幂等初始化入口。</summary>
        public void Initialize()
        {
            if (initialized)
                return;
            initialized = true;
            interaction.RefreshRequested += OnRefresh;
            interaction.LoadMoreRequested += OnLoadMore;
            interaction.PreloadRequested += OnPreload;
            refresh.onClick.AddListener(RequestRefresh);
            loadMore.onClick.AddListener(RequestLoadMore);
            preload.onClick.AddListener(TogglePreload);
            complete.onClick.AddListener(CompletePending);
            noMore.onClick.AddListener(SetNoMore);
            nextSection.onClick.AddListener(NextSection);
            insert.onClick.AddListener(InsertItem);
            move.onClick.AddListener(MoveItem);
            remove.onClick.AddListener(RemoveItem);
            Canvas.ForceUpdateCanvases();
            collection.ApplySnapshot(CreateData());
            UpdateStatistics();
        }

        public static CollectionSnapshot CreateData()
        {
            var snapshot = new CollectionSnapshot();
            for (int s = 0; s < 3; s++)
            {
                var section = new CollectionSection
                {
                    Id = "interaction-section-" + s,
                    Layout = new SectionLayoutSettings
                    {
                        Mode = LayoutMode.Columns,
                        ColumnCount = 2,
                        Insets = new Insets(16, 16, 16, 16),
                        HorizontalSpacing = 16,
                        VerticalSpacing = 16,
                        SectionSpacing = 24
                    },
                    Header = new CollectionItem
                    {
                        Id = "header",
                        ReuseIdentifier = "header",
                        Size = ItemSize.Fixed(0, 56),
                        Data = new DemoCard
                        {
                            Title = "SECTION " + (s + 1) + " / STICKY HEADER"
                        }
                    }
                };
                for (int i = 0; i < 10; i++)
                    section.Items.Add(CreateItem(s * 10 + i));
                snapshot.Sections.Add(section);
            }

            return snapshot;
        }

        static CollectionItem CreateItem(int id) => new CollectionItem
        {
            Id = "interaction-item-" + id,
            ReuseIdentifier = "card",
            Size = ItemSize.Fixed(0, 120 + id % 4 * 32),
            Data = new DemoCard
            {
                Title = "CARD " + id + "\nStable ID / variable height"
            }
        };
        void RequestRefresh() => interaction.BeginRefresh();
        void RequestLoadMore() => interaction.BeginLoadMore();
        void OnRefresh(long id)
        {
            refreshRequests++;
            UpdateStatistics();
        }

        void OnLoadMore(long id)
        {
            loadRequests++;
            UpdateStatistics();
        }

        void OnPreload(long id)
        {
            preloadRequests++;
            UpdateStatistics();
        }

        void TogglePreload()
        {
            interaction.PreloadEnabled = !interaction.PreloadEnabled;
            UpdateStatistics();
        }

        /// <summary>业务先提交数据，再使用同一请求标识结束 loading。</summary>
        public void CompletePending()
        {
            long id = interaction.ActiveRequestId;
            if (id == 0)
                return;
            if (interaction.IsRefreshing)
            {
                collection.ApplySnapshot(CreateData());
                serial = 30;
                interaction.HasMore = true;
                // 保留提交时的锚点和越界量，由 Loader 平滑回弹；立即定位会造成单帧跳动。
                interaction.CompleteRefresh(id);
            }
            else
            {
                var snapshot = collection.GetSnapshotCopy();
                var section = snapshot.Sections[snapshot.Sections.Count - 1];
                var changes = new CollectionChangeSet();
                for (int i = 0; i < 10; i++)
                    changes.InsertItem(section.Id, section.Items.Count + i, CreateItem(serial++));
                collection.ApplyChanges(changes);
                if (interaction.IsLoadingMore)
                    interaction.CompleteLoadMore(id);
                else
                    interaction.CompletePreload(id);
            }

            UpdateStatistics();
        }

        public void SetNoMore()
        {
            long id = interaction.ActiveRequestId;
            if (interaction.IsLoadingMore)
                interaction.CompleteLoadMore(id, false);
            else if (interaction.IsPreloading)
                interaction.CompletePreload(id, false);
            else
                interaction.HasMore = false;
            UpdateStatistics();
        }

        void NextSection()
        {
            sectionIndex = (sectionIndex + 1) % 3;
            collection.ScrollToSection("interaction-section-" + sectionIndex, true);
        }

        void InsertItem()
        {
            collection.ApplyChanges(new CollectionChangeSet().InsertItem("interaction-section-0", 0, CreateItem(serial++)));
            UpdateStatistics();
        }

        void MoveItem()
        {
            var snapshot = collection.GetSnapshotCopy();
            foreach (var section in snapshot.Sections)
            {
                if (section.Items.Count == 0)
                    continue;
                string target = section.Id == "interaction-section-2" ? "interaction-section-0" : "interaction-section-2";
                collection.ApplyChanges(new CollectionChangeSet().MoveItem(section.Items[0].Id, target, 0));
                break;
            }

            UpdateStatistics();
        }

        void RemoveItem()
        {
            foreach (var section in collection.GetSnapshotCopy().Sections)
            {
                if (section.Items.Count == 0)
                    continue;
                collection.ApplyChanges(new CollectionChangeSet().RemoveItem(section.Items[0].Id));
                break;
            }

            UpdateStatistics();
        }

        void UpdateStatistics()
        {
            int count = 0;
            foreach (var section in collection.GetSnapshotCopy().Sections)
                count += section.Items.Count;
            statistics.text = count + " items | Requests R/L/P: " + refreshRequests + "/" + loadRequests + "/" + preloadRequests + "\nPending: " + interaction.ActiveRequestId + " | No more: " + (!interaction.HasMore ? "YES" : "NO") + " | Complete manually";
            preload.GetComponentInChildren<Text>().text = "PRELOAD " + (interaction.PreloadEnabled ? "ON" : "OFF");
        }

        void OnDestroy()
        {
            if (!initialized)
                return;
            interaction.RefreshRequested -= OnRefresh;
            interaction.LoadMoreRequested -= OnLoadMore;
            interaction.PreloadRequested -= OnPreload;
            refresh.onClick.RemoveListener(RequestRefresh);
            loadMore.onClick.RemoveListener(RequestLoadMore);
            preload.onClick.RemoveListener(TogglePreload);
            complete.onClick.RemoveListener(CompletePending);
            noMore.onClick.RemoveListener(SetNoMore);
            nextSection.onClick.RemoveListener(NextSection);
            insert.onClick.RemoveListener(InsertItem);
            move.onClick.RemoveListener(MoveItem);
            remove.onClick.RemoveListener(RemoveItem);
        }
    }
}
