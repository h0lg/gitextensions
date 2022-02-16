using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.Drawing.Imaging;
using System.Linq;
using System.Reflection;
using System.Windows.Forms;
using GitCommands;
using GitCommands.Git;
using GitExtUtils.GitUI;
using GitExtUtils.GitUI.Theming;
using GitUI.CommandsDialogs;
using GitUI.Hotkey;
using GitUI.Properties;
using GitUI.Script;
using GitUI.UserControls;
using GitUI.UserControls.RevisionGrid;
using GitUIPluginInterfaces;
using ResourceManager;

namespace GitUI.BranchTreePanel
{
    public partial class RepoObjectsTree : GitModuleControl
    {
        public const string HotkeySettingsName = "LeftPanel";

        private readonly CancellationTokenSequence _selectionCancellationTokenSequence = new();
        private readonly TranslationString _searchTooltip = new("Search");

        private readonly TranslationString _showSelectedBranchesOnly = new(
            "Filter the revision grid to show selected (underlined) branches only." +
            "\nUse Ctrl + left click in these branch trees to extend or narrow the selection." +
            "\nTo reset the filter, right click the revision grid, select 'View' and then 'Show all branches'.");

        private NativeTreeViewDoubleClickDecorator _doubleClickDecorator;
        private NativeTreeViewExplorerNavigationDecorator _explorerNavigationDecorator;
        private readonly List<Tree> _rootNodes = new();
        private readonly SearchControl<string> _txtBranchCriterion;
        private BranchTree _branchesTree;
        private RemoteBranchTree _remotesTree;
        private TagTree _tagTree;
        private SubmoduleTree _submoduleTree;
        private List<TreeNode>? _searchResult;
        private Action<string?> _branchFilterAction;
        private IAheadBehindDataProvider? _aheadBehindDataProvider;
        private bool _searchCriteriaChanged;
        private ICheckRefs _refsSource;
        private IScriptHostControl _scriptHost;
        private IRunScript _scriptRunner;

        public RepoObjectsTree()
        {
            Disposed += (s, e) => _selectionCancellationTokenSequence.Dispose();

            InitializeComponent();
            InitImageList();
            _txtBranchCriterion = CreateSearchBox();
            branchSearchPanel.Controls.Add(_txtBranchCriterion, 1, 0);

            mnubtnCollapse.AdaptImageLightness();
            tsbCollapseAll.AdaptImageLightness();
            mnubtnExpand.AdaptImageLightness();
            mnubtnFetchAllBranchesFromARemote.AdaptImageLightness();
            mnuBtnPruneAllBranchesFromARemote.AdaptImageLightness();
            mnuBtnFetchAllRemotes.AdaptImageLightness();
            mnuBtnPruneAllRemotes.AdaptImageLightness();
            mnubtnFetchCreateBranch.AdaptImageLightness();
            mnubtnPullFromRemoteBranch.AdaptImageLightness();
            InitializeComplete();

            ReloadHotkeys();
            HotkeysEnabled = true;

            RegisterContextActions();

            treeMain.ShowNodeToolTips = true;

            toolTip.SetToolTip(btnSearch, _searchTooltip.Text);
            tsbCollapseAll.ToolTipText = mnubtnCollapse.ToolTipText;

            tsbShowBranches.Checked = AppSettings.RepoObjectsTreeShowBranches;
            tsbShowRemotes.Checked = AppSettings.RepoObjectsTreeShowRemotes;
            tsbShowTags.Checked = AppSettings.RepoObjectsTreeShowTags;
            tsbShowSubmodules.Checked = AppSettings.RepoObjectsTreeShowSubmodules;

            _doubleClickDecorator = new NativeTreeViewDoubleClickDecorator(treeMain);
            _doubleClickDecorator.BeforeDoubleClickExpandCollapse += BeforeDoubleClickExpandCollapse;

            _explorerNavigationDecorator = new NativeTreeViewExplorerNavigationDecorator(treeMain);
            _explorerNavigationDecorator.AfterSelect += OnNodeSelected;

            treeMain.NodeMouseClick += OnNodeClick;
            treeMain.NodeMouseDoubleClick += OnNodeDoubleClick;

            mnubtnFilterRemoteBranchInRevisionGrid.ToolTipText =
            mnubtnFilterLocalBranchInRevisionGrid.ToolTipText = _showSelectedBranchesOnly.Text;

            return;

            void InitImageList()
            {
                const int rowPadding = 1; // added to top and bottom, so doubled -- this value is scaled *after*, so consider 96dpi here

                treeMain.ImageList = new ImageList
                {
                    ColorDepth = ColorDepth.Depth32Bit,
                    ImageSize = DpiUtil.Scale(new Size(16, 16 + rowPadding + rowPadding)), // Scale ImageSize and images scale automatically
                    Images =
                    {
                        { nameof(Images.ArrowUp), Pad(Images.ArrowUp) },
                        { nameof(Images.ArrowDown), Pad(Images.ArrowDown) },
                        { nameof(Images.FolderClosed), Pad(Images.FolderClosed) },
                        { nameof(Images.BranchLocal), Pad(Images.BranchLocal) },
                        { nameof(Images.BranchLocalMerged), Pad(Images.BranchLocalMerged) },
                        { nameof(Images.Branch), Pad(Images.Branch.AdaptLightness()) },
                        { nameof(Images.Remote), Pad(Images.Remote) },
                        { nameof(Images.BitBucket), Pad(Images.BitBucket) },
                        { nameof(Images.GitHub), Pad(Images.GitHub) },
                        { nameof(Images.VisualStudioTeamServices), Pad(Images.VisualStudioTeamServices) },
                        { nameof(Images.BranchLocalRoot), Pad(Images.BranchLocalRoot) },
                        { nameof(Images.BranchRemoteRoot), Pad(Images.BranchRemoteRoot) },
                        { nameof(Images.BranchRemote), Pad(Images.BranchRemote) },
                        { nameof(Images.BranchRemoteMerged), Pad(Images.BranchRemoteMerged) },
                        { nameof(Images.BranchFolder), Pad(Images.BranchFolder) },
                        { nameof(Images.TagHorizontal), Pad(Images.TagHorizontal) },
                        { nameof(Images.EyeOpened), Pad(Images.EyeOpened) },
                        { nameof(Images.EyeClosed), Pad(Images.EyeClosed) },
                        { nameof(Images.RemoteEnableAndFetch), Pad(Images.RemoteEnableAndFetch) },
                        { nameof(Images.FileStatusModified), Pad(Images.FileStatusModified) },
                        { nameof(Images.FolderSubmodule), Pad(Images.FolderSubmodule) },
                        { nameof(Images.SubmoduleDirty), Pad(Images.SubmoduleDirty) },
                        { nameof(Images.SubmoduleRevisionUp), Pad(Images.SubmoduleRevisionUp) },
                        { nameof(Images.SubmoduleRevisionDown), Pad(Images.SubmoduleRevisionDown) },
                        { nameof(Images.SubmoduleRevisionSemiUp), Pad(Images.SubmoduleRevisionSemiUp) },
                        { nameof(Images.SubmoduleRevisionSemiDown), Pad(Images.SubmoduleRevisionSemiDown) },
                        { nameof(Images.SubmoduleRevisionUpDirty), Pad(Images.SubmoduleRevisionUpDirty) },
                        { nameof(Images.SubmoduleRevisionDownDirty), Pad(Images.SubmoduleRevisionDownDirty) },
                        { nameof(Images.SubmoduleRevisionSemiUpDirty), Pad(Images.SubmoduleRevisionSemiUpDirty) },
                        { nameof(Images.SubmoduleRevisionSemiDownDirty), Pad(Images.SubmoduleRevisionSemiDownDirty) },
                    }
                };
                treeMain.SelectedImageKey = treeMain.ImageKey;

                Image Pad(Image image)
                {
                    Bitmap padded = new(image.Width, image.Height + rowPadding + rowPadding, PixelFormat.Format32bppArgb);
                    using var g = Graphics.FromImage(padded);
                    g.DrawImageUnscaled(image, 0, rowPadding);
                    return padded;
                }
            }

            SearchControl<string> CreateSearchBox()
            {
                SearchControl<string> search = new(SearchForBranch, i => { })
                {
                    Anchor = AnchorStyles.Left | AnchorStyles.Right,
                    Name = "txtBranchCritierion",
                    TabIndex = 1
                };
                search.OnTextEntered += () =>
                {
                    OnBranchCriterionChanged(this, EventArgs.Empty);
                    OnBtnSearchClicked(this, EventArgs.Empty);
                };
                search.TextChanged += OnBranchCriterionChanged;
                search.KeyDown += TxtBranchCriterion_KeyDown;

                search.SearchBoxBorderStyle = BorderStyle.FixedSingle;
                search.SearchBoxBorderDefaultColor = Color.LightGray.AdaptBackColor();
                search.SearchBoxBorderHoveredColor = SystemColors.Highlight.AdaptBackColor();
                search.SearchBoxBorderFocusedColor = SystemColors.HotTrack.AdaptBackColor();

                return search;

                IEnumerable<string> SearchForBranch(string arg)
                {
                    return CollectFilterCandidates()
                        .Where(r => r.IndexOf(arg, StringComparison.OrdinalIgnoreCase) != -1);
                }

                IEnumerable<string> CollectFilterCandidates()
                {
                    List<string> list = new();

                    foreach (TreeNode rootNode in treeMain.Nodes)
                    {
                        CollectFromNodes(rootNode.Nodes);
                    }

                    return list;

                    void CollectFromNodes(TreeNodeCollection nodes)
                    {
                        foreach (TreeNode node in nodes)
                        {
                            if (node.Tag is BaseBranchNode branch)
                            {
                                if (!branch.HasChildren())
                                {
                                    list.Add(branch.FullPath);
                                }
                            }
                            else
                            {
                                list.Add(node.Text);
                            }

                            CollectFromNodes(node.Nodes);
                        }
                    }
                }
            }
        }

        private void BeforeDoubleClickExpandCollapse(object sender, CancelEventArgs e)
        {
            // If node is an inner node, and overrides OnDoubleClick, then disable expand/collapse
            if (treeMain.SelectedNode?.Tag is Node node && node.HasChildren()
                && IsOverride(node.GetType().GetMethod(nameof(OnDoubleClick), BindingFlags.Instance | BindingFlags.NonPublic)))
            {
                e.Cancel = true;
            }

            return;

            bool IsOverride(MethodInfo m)
            {
                return m is not null && m.GetBaseDefinition().DeclaringType != m.DeclaringType;
            }
        }

        public void Initialize(IAheadBehindDataProvider? aheadBehindDataProvider, Action<string?> branchFilterAction, ICheckRefs refsSource, IScriptHostControl scriptHost, IRunScript scriptRunner)
        {
            _aheadBehindDataProvider = aheadBehindDataProvider;
            _branchFilterAction = branchFilterAction;
            _refsSource = refsSource;
            _scriptHost = scriptHost;
            _scriptRunner = scriptRunner;

            // This lazily sets the command source, invoking OnUICommandsSourceSet, which is required for setting up
            // notifications for each Tree.
            _ = UICommandsSource;
        }

        /// <summary>
        /// Toggles filtering mode on or off to the git refs present in the left panel depending on the app's global filtering rules .
        /// These rules include: show all branches / show current branch / show filtered branches, etc.
        /// </summary>
        /// <param name="isFiltering">
        ///  <see langword="true"/>, if the data is being filtered; otherwise <see langword="false"/>.
        /// </param>
        /// <param name="getRefs">Function to get refs.</param>
        /// <param name="forceRefresh">Refresh may be required as references may have been changed.</param>
        public void Refresh(bool isFiltering, bool forceRefresh, Func<RefsFilter, IReadOnlyList<IGitRef>> getRefs)
        {
            _branchesTree.Refresh(isFiltering, forceRefresh, getRefs);
            _remotesTree.Refresh(isFiltering, forceRefresh, getRefs);
            _tagTree.Refresh(isFiltering, forceRefresh, getRefs);
        }

        public void Refresh(Func<RefsFilter, IReadOnlyList<IGitRef>> getRefs)
        {
            _branchesTree.Refresh(getRefs);
            _remotesTree.Refresh(getRefs);
            _tagTree.Refresh(getRefs);
        }

        public void ReloadHotkeys()
        {
            Hotkeys = HotkeySettingsManager.LoadHotkeys(HotkeySettingsName);
        }

        public void SelectionChanged(IReadOnlyList<GitRevision> selectedRevisions)
        {
            // If we arrived here through the chain of events after selecting a node in the tree,
            // and the selected revision is the one we have selected - do nothing.
            if ((selectedRevisions.Count == 0 && treeMain.SelectedNode is null)
                || (selectedRevisions.Count == 1 && selectedRevisions[0].ObjectId == GetSelectedNodeObjectId(treeMain.SelectedNode)))
            {
                return;
            }

            var cancellationToken = _selectionCancellationTokenSequence.Next();

            GitRevision selectedRevision = selectedRevisions.FirstOrDefault();
            string? selectedGuid = selectedRevision is null
                ? null
                : selectedRevision.IsArtificial
                    ? "HEAD"
                    : selectedRevision.Guid;
            ThreadHelper.JoinableTaskFactory.RunAsync(async () =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                HashSet<string> mergedBranches = selectedGuid is null
                    ? new HashSet<string>()
                    : (await Module.GetMergedBranchesAsync(includeRemote: true, fullRefname: true, commit: selectedGuid)).ToHashSet();

                selectedRevision?.Refs.ForEach(gitRef => mergedBranches.Remove(gitRef.CompleteName));

                await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync(cancellationToken);

                try
                {
                    treeMain.BeginUpdate();
                    cancellationToken.ThrowIfCancellationRequested();
                    _branchesTree?.DepthEnumerator<BaseBranchLeafNode>().ForEach(node => node.IsMerged = mergedBranches.Contains(GitRefName.RefsHeadsPrefix + node.FullPath));
                    cancellationToken.ThrowIfCancellationRequested();
                    _remotesTree?.DepthEnumerator<BaseBranchLeafNode>().ForEach(node => node.IsMerged = mergedBranches.Contains(GitRefName.RefsRemotesPrefix + node.FullPath));
                }
                finally
                {
                    treeMain.EndUpdate();
                }
            }).FileAndForget();

            static ObjectId? GetSelectedNodeObjectId(TreeNode treeNode)
            {
                // Local or remote branch nodes or tag nodes
                return Node.GetNodeSafe<BaseBranchLeafNode>(treeNode)?.ObjectId ??
                    Node.GetNodeSafe<TagNode>(treeNode)?.ObjectId;
            }
        }

        protected override void OnUICommandsSourceSet(IGitUICommandsSource source)
        {
            _selectionCancellationTokenSequence.CancelCurrent();

            base.OnUICommandsSourceSet(source);

            CreateBranches();
            CreateRemotes();
            CreateTags();
            CreateSubmodules();

            FixInvalidTreeToPositionIndices();
            ShowEnabledTrees();
        }

        private static void AddTreeNodeToSearchResult(ICollection<TreeNode> ret, TreeNode node)
        {
            node.BackColor = SystemColors.Info;
            node.ForeColor = SystemColors.InfoText;
            ret.Add(node);
        }

        private void CreateBranches()
        {
            TreeNode rootNode = new(TranslatedStrings.Branches)
            {
                Name = TranslatedStrings.Branches,
                ImageKey = nameof(Images.BranchLocalRoot),
                SelectedImageKey = nameof(Images.BranchLocalRoot),
            };
            _branchesTree = new BranchTree(rootNode, UICommandsSource, _aheadBehindDataProvider, _refsSource);
        }

        private void CreateRemotes()
        {
            TreeNode rootNode = new(TranslatedStrings.Remotes)
            {
                Name = TranslatedStrings.Remotes,
                ImageKey = nameof(Images.BranchRemoteRoot),
                SelectedImageKey = nameof(Images.BranchRemoteRoot),
            };
            _remotesTree = new RemoteBranchTree(rootNode, UICommandsSource, _refsSource)
            {
                TreeViewNode =
                {
                    ContextMenuStrip = menuRemotes
                }
            };
        }

        private void CreateTags()
        {
            TreeNode rootNode = new(TranslatedStrings.Tags)
            {
                Name = TranslatedStrings.Tags,
                ImageKey = nameof(Images.TagHorizontal),
                SelectedImageKey = nameof(Images.TagHorizontal),
            };
            _tagTree = new TagTree(rootNode, UICommandsSource, _refsSource);
        }

        private void CreateSubmodules()
        {
            TreeNode rootNode = new(TranslatedStrings.Submodules)
            {
                Name = TranslatedStrings.Submodules,
                ImageKey = nameof(Images.FolderSubmodule),
                SelectedImageKey = nameof(Images.FolderSubmodule),
            };
            _submoduleTree = new SubmoduleTree(rootNode, UICommandsSource);
        }

        private void AddTree(Tree tree)
        {
            tree.TreeViewNode.SelectedImageKey = tree.TreeViewNode.ImageKey;
            tree.TreeViewNode.Tag = tree;

            // Add Tree's node in position index order. Because TreeNodeCollections cannot be sorted,
            // we create a list from it, sort it, then clear and re-add the nodes back to the collection.
            treeMain.BeginUpdate();
            List<TreeNode> nodeList = treeMain.Nodes.OfType<TreeNode>().ToList();
            nodeList.Add(tree.TreeViewNode);
            treeMain.Nodes.Clear();
            var treeToPositionIndex = GetTreeToPositionIndex();
            treeMain.Nodes.AddRange(nodeList.OrderBy(treeNode => treeToPositionIndex[(Tree)treeNode.Tag]).ToArray());
            treeMain.EndUpdate();

            treeMain.Font = AppSettings.Font;
            _rootNodes.Add(tree);

            tree.Attached();
        }

        private void RemoveTree(Tree tree)
        {
            tree.Detached();
            _rootNodes.Remove(tree);
            treeMain.Nodes.Remove(tree.TreeViewNode);
        }

        private void DoSearch()
        {
            _txtBranchCriterion.CloseDropdown();

            if (_searchCriteriaChanged && _searchResult is not null && _searchResult.Any())
            {
                _searchCriteriaChanged = false;
                foreach (var coloredNode in _searchResult)
                {
                    coloredNode.BackColor = SystemColors.Window;
                }

                _searchResult = null;
                if (string.IsNullOrWhiteSpace(_txtBranchCriterion.Text))
                {
                    _txtBranchCriterion.Focus();
                    return;
                }
            }

            if (_searchResult is null || !_searchResult.Any())
            {
                if (!string.IsNullOrWhiteSpace(_txtBranchCriterion.Text))
                {
                    _searchResult = SearchTree(_txtBranchCriterion.Text, treeMain.Nodes.Cast<TreeNode>());
                }
            }

            var node = GetNextSearchResult();

            if (node is null)
            {
                return;
            }

            node.EnsureVisible();
            treeMain.SelectedNode = node;

            return;

            TreeNode? GetNextSearchResult()
            {
                var first = _searchResult?.FirstOrDefault();

                if (first is null)
                {
                    return null;
                }

                _searchResult!.RemoveAt(0);
                _searchResult.Add(first);
                return first;
            }

            List<TreeNode> SearchTree(string text, IEnumerable<TreeNode> nodes)
            {
                Queue<TreeNode> queue = new(nodes);
                List<TreeNode> ret = new();

                while (queue.Count != 0)
                {
                    var n = queue.Dequeue();

                    if (n.Tag is BaseBranchNode branch)
                    {
                        if (branch.FullPath.IndexOf(text, StringComparison.InvariantCultureIgnoreCase) != -1)
                        {
                            AddTreeNodeToSearchResult(ret, n);
                        }
                    }
                    else
                    {
                        if (n.Text.IndexOf(text, StringComparison.InvariantCultureIgnoreCase) != -1)
                        {
                            AddTreeNodeToSearchResult(ret, n);
                        }
                    }

                    foreach (TreeNode subNode in n.Nodes)
                    {
                        queue.Enqueue(subNode);
                    }
                }

                return ret;
            }
        }

        private void OnBtnSearchClicked(object sender, EventArgs e)
        {
            DoSearch();
        }

        private void btnCollapseAll_Click(object sender, EventArgs e)
        {
            treeMain.CollapseAll();
        }

        private void OnBranchCriterionChanged(object sender, EventArgs e)
        {
            _searchCriteriaChanged = true;
        }

        private void TxtBranchCriterion_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode != Keys.Enter)
            {
                return;
            }

            OnBtnSearchClicked(this, EventArgs.Empty);
            e.Handled = true;
        }

        protected override CommandStatus ExecuteCommand(int cmd)
        {
            switch ((Command)cmd)
            {
                case Command.Delete: Node.OnNode<Node>(treeMain.SelectedNode, node => node.OnDelete()); return true;
                case Command.Rename: Node.OnNode<Node>(treeMain.SelectedNode, node => node.OnRename()); return true;
                case Command.Search: OnBtnSearchClicked(this, EventArgs.Empty); return true;
                case Command.MultiSelect:
                case Command.MultiSelectWithChildren:
                    OnNodeClick(this, new TreeNodeMouseClickEventArgs(treeMain.SelectedNode, MouseButtons.Left, clicks: 1, x: 0, y: 0));
                    return true;
                default: return base.ExecuteCommand(cmd);
            }
        }

        private void OnNodeSelected(object sender, TreeViewEventArgs e)
        {
            Node.OnNode<Node>(e.Node, node => node.OnSelected());
        }

        private static IEnumerable<Node> GetMultiSelectedIn(params Tree[] trees) => trees.GetMultiSelection();
        private IEnumerable<Node> GetMultiSelection() => _rootNodes.GetMultiSelection();
        private IEnumerable<NodeBase> GetSelectedNodes() => GetMultiSelection().Append(treeMain.SelectedNode.Tag as NodeBase).Distinct();
        private IEnumerable<BaseBranchLeafNode> GetSelectedBranches() => GetMultiSelectedIn(_branchesTree, _remotesTree).OfType<BaseBranchLeafNode>();
        private void RevertMultiSelection() => GetMultiSelection().ForEach(node => node.MultiSelect(false));

        private void OnNodeClick(object sender, TreeNodeMouseClickEventArgs e)
        {
            var holdingCtrl = ModifierKeys == Keys.Control;

            if (e.Node.Tag is Tree)
            {
                if (!holdingCtrl)
                {
                    RevertMultiSelection();
                }

                return;
            }

            var node = Node.GetNodeSafe<Node>(e.Node);

            if (node is null || (e.Button == MouseButtons.Right && node.IsMultiSelected))
            {
                return; // don't undo multi-selection on opening context menu
            }

            if (holdingCtrl)
            {
                // toggle clicked node IsMultiSelected, including descendants
                node.MultiSelect(!node.IsMultiSelected, includingDescendants: true);
            }
            else
            {
                RevertMultiSelection(); // deselect all selected nodes
                node.MultiSelect(true); // and only check the clicked one
            }

            node.OnClick();
        }

        private void OnNodeDoubleClick(object sender, TreeNodeMouseClickEventArgs e)
        {
            // Don't consider-double clicking on the PlusMinus as a double-click event
            // for nodes in tree. This prevents opening inner submodules, for example,
            // when quickly collapsing/expanding them.
            var hitTest = treeMain.HitTest(e.Location);
            if (hitTest.Location == TreeViewHitTestLocations.PlusMinus)
            {
                return;
            }

            // Don't use e.Node, when folding/unfolding a node,
            // e.Node won't be the one you double clicked, but a child node instead
            Node.OnNode<Node>(treeMain.SelectedNode, node => node.OnDoubleClick());
        }

        internal TestAccessor GetTestAccessor()
            => new(this);

        internal readonly struct TestAccessor
        {
            private readonly RepoObjectsTree _repoObjectsTree;

            public TestAccessor(RepoObjectsTree repoObjectsTree)
            {
                _repoObjectsTree = repoObjectsTree;
            }

            public ContextMenuStrip BranchContextMenu => _repoObjectsTree.menuBranch;
            public ContextMenuStrip RemoteContextMenu => _repoObjectsTree.menuRemote;
            public ContextMenuStrip TagContextMenu => _repoObjectsTree.menuTag;
            public NativeTreeView TreeView => _repoObjectsTree.treeMain;

            public void OnContextMenuOpening(object sender, CancelEventArgs e) => _repoObjectsTree.contextMenu_Opening(sender, e);

            public void ReorderTreeNode(TreeNode node, bool up)
            {
                _repoObjectsTree.ReorderTreeNode(node, up);
            }

            public void SetTreeVisibleByIndex(int index, bool visible)
            {
                var tree = _repoObjectsTree.GetTreeToPositionIndex().FirstOrDefault(kvp => kvp.Value == index).Key;
                if (tree.TreeViewNode.IsVisible == visible)
                {
                    return;
                }

                if (visible)
                {
                    _repoObjectsTree.AddTree(tree);
                }
                else
                {
                    _repoObjectsTree.RemoveTree(tree);
                }
            }
        }
    }
}
