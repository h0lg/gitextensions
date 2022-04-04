﻿using System;
using System.ComponentModel;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using GitCommands;
using GitUI.BranchTreePanel.ContextMenu;
using GitUI.BranchTreePanel.Interfaces;
using GitUI.CommandsDialogs;
using GitUIPluginInterfaces;
using ResourceManager;

namespace GitUI.BranchTreePanel
{
    partial class RepoObjectsTree : IMenuItemFactory
    {
        private GitRefsSortOrderContextMenuItem _sortOrderContextMenuItem;
        private GitRefsSortByContextMenuItem _sortByContextMenuItem;

        /// <summary>
        /// Local branch context menu [git ref / rename / delete] actions.
        /// </summary>
        private LocalBranchMenuItems<LocalBranchNode> _localBranchMenuItems;

        /// <summary>
        /// Remote branch context menu [git ref / rename / delete] actions.
        /// </summary>
        private MenuItemsGenerator<RemoteBranchNode> _remoteBranchMenuItems;

        /// <summary>
        /// Tags context menu [git ref] actions.
        /// </summary>
        private MenuItemsGenerator<TagNode> _tagNodeMenuItems;

        private void EnableMenuItems(bool enabled, params ToolStripItem[] items)
        {
            foreach (var item in items)
            {
                item.Enable(enabled);
            }
        }

        private void EnableMenuItems<TNode>(MenuItemsGenerator<TNode> generator, Func<ToolStripItemWithKey, bool> isEnabled) where TNode : class, INode
        {
            foreach (var item in generator)
            {
                item.Item.Enable(isEnabled(item));
            }
        }

        /* add Expand All / Collapse All menu entry
         * depending on whether node is expanded or collapsed and has child nodes at all */
        private void EnableExpandCollapseContextMenu(NodeBase[] selectedNodes)
        {
            var multiSelectedParents = selectedNodes.HavingChildren().ToArray();
            mnubtnExpand.Visible = mnubtnCollapse.Visible = multiSelectedParents.Length > 0;
            mnubtnExpand.Enabled = multiSelectedParents.Expandable().Any();
            mnubtnCollapse.Enabled = multiSelectedParents.Collapsible().Any();
        }

        private void EnableMoveTreeUpDownContexMenu(bool hasSingleSelection, NodeBase selectedNode)
        {
            var isSingleTreeSelected = hasSingleSelection && selectedNode is Tree;
            var treeNode = (selectedNode as Tree)?.TreeViewNode;
            mnubtnMoveUp.Visible = mnubtnMoveDown.Visible = isSingleTreeSelected;
            mnubtnMoveUp.Enabled = isSingleTreeSelected && treeNode.PrevNode is not null;
            mnubtnMoveDown.Enabled = isSingleTreeSelected && treeNode.NextNode is not null;
        }

        private void EnableRemoteBranchContextMenu(bool hasSingleSelection, NodeBase selectedNode)
        {
            var isSingleRemoteBranchSelected = hasSingleSelection && selectedNode is RemoteBranchNode;
            EnableMenuItems(_remoteBranchMenuItems, _ => isSingleRemoteBranchSelected);

            EnableMenuItems(isSingleRemoteBranchSelected, mnubtnFetchOneBranch, mnubtnPullFromRemoteBranch,
                mnubtnRemoteBranchFetchAndCheckout, mnubtnFetchCreateBranch, mnubtnFetchRebase);
        }

        private void EnableRemoteRepoContextMenu(bool hasSingleSelection, NodeBase selectedNode)
        {
            var isSingleRemoteRepoSelected = hasSingleSelection && selectedNode is RemoteRepoNode;
            var remoteRepo = selectedNode as RemoteRepoNode;
            mnubtnManageRemotes.Enable(isSingleRemoteRepoSelected);
            EnableMenuItems(isSingleRemoteRepoSelected && remoteRepo.Enabled, mnubtnFetchAllBranchesFromARemote, mnubtnDisableRemote, mnuBtnPruneAllBranchesFromARemote);
            mnuBtnOpenRemoteUrlInBrowser.Enable(isSingleRemoteRepoSelected && remoteRepo.IsRemoteUrlUsingHttp);
            EnableMenuItems(isSingleRemoteRepoSelected && !remoteRepo.Enabled, mnubtnEnableRemote, mnubtnEnableRemoteAndFetch);
        }

        private void EnableSortContextMenu(bool hasSingleSelection, NodeBase selectedNode)
        {
            var isSingleRefSelected = hasSingleSelection && selectedNode is IGitRefActions;
            _sortByContextMenuItem.Enable(isSingleRefSelected);

            // If refs are sorted by git (GitRefsSortBy = Default) don't show sort order options
            var showSortOrder = AppSettings.RefsSortBy != GitRefsSortBy.Default;
            _sortOrderContextMenuItem.Enable(isSingleRefSelected && showSortOrder);
        }

        private void EnableSubmoduleContextMenu(bool hasSingleSelection, NodeBase selectedNode)
        {
            var isSingleSubmoduleSelected = hasSingleSelection && selectedNode is SubmoduleNode;
            var submoduleNode = selectedNode as SubmoduleNode;
            var bareRepository = Module.IsBareRepository();
            EnableMenuItems(isSingleSubmoduleSelected && submoduleNode.CanOpen, mnubtnOpenSubmodule, mnubtnOpenGESubmodule);
            mnubtnUpdateSubmodule.Enable(isSingleSubmoduleSelected);
            EnableMenuItems(isSingleSubmoduleSelected && !bareRepository && submoduleNode.IsCurrent, mnubtnManageSubmodules, mnubtnSynchronizeSubmodules);
            EnableMenuItems(isSingleSubmoduleSelected && !bareRepository, mnubtnResetSubmodule, mnubtnStashSubmodule, mnubtnCommitSubmodule);
        }

        private static void RegisterClick(ToolStripItem item, Action onClick)
        {
            item.Click += (o, e) => onClick();
        }

        private void RegisterClick<T>(ToolStripItem item, Action<T> onClick) where T : class, INode
        {
            item.Click += (o, e) => Node.OnNode(treeMain.SelectedNode, onClick);
        }

        private void RegisterContextActions()
        {
            copyContextMenuItem.SetRevisionFunc(() => _scriptHost.GetSelectedRevisions());

            filterForSelectedRefsMenuItem.ToolTipText = "Filter the revision grid to show selected (underlined) refs (branches and tags) only." +
                "\nHold CTRL while clicking to de/select multiple and include descendant tree nodes by additionally holding SHIFT." +
                "\nReset the filter via View > Show all branches.";

            RegisterClick(filterForSelectedRefsMenuItem, () =>
            {
                var refPaths = GetMultiSelection().OfType<IGitRefActions>().Select(b => b.FullPath);
                _filterRevisionGridBySpaceSeparatedRefs(refPaths.Join(" "));
            });

            // git refs (tag, local & remote branch) menu items (rename, delete, merge, etc)
            _tagNodeMenuItems = new TagMenuItems<TagNode>(this);
            _remoteBranchMenuItems = new RemoteBranchMenuItems<RemoteBranchNode>(this);
            _localBranchMenuItems = new LocalBranchMenuItems<LocalBranchNode>(this);
            menuMain.InsertItems(_tagNodeMenuItems.Select(s => s.Item).Prepend(new ToolStripSeparator()), after: filterForSelectedRefsMenuItem);
            menuMain.InsertItems(_remoteBranchMenuItems.Select(s => s.Item).Prepend(new ToolStripSeparator()), after: filterForSelectedRefsMenuItem);
            menuMain.InsertItems(_localBranchMenuItems.Select(s => s.Item).Prepend(new ToolStripSeparator()), after: filterForSelectedRefsMenuItem);

            // Remotes Tree
            RegisterClick(mnuBtnManageRemotesFromRootNode, () => _remotesTree.PopupManageRemotesForm(remoteName: null));
            RegisterClick(mnuBtnFetchAllRemotes, () => _remotesTree.FetchAll());
            RegisterClick(mnuBtnPruneAllRemotes, () => _remotesTree.FetchPruneAll());

            // RemoteRepoNode
            RegisterClick<RemoteRepoNode>(mnubtnManageRemotes, remoteBranch => remoteBranch.PopupManageRemotesForm());
            RegisterClick<RemoteRepoNode>(mnubtnFetchAllBranchesFromARemote, remote => remote.Fetch());
            RegisterClick<RemoteRepoNode>(mnuBtnPruneAllBranchesFromARemote, remote => remote.Prune());
            RegisterClick<RemoteRepoNode>(mnuBtnOpenRemoteUrlInBrowser, remote => remote.OpenRemoteUrlInBrowser());
            RegisterClick<RemoteRepoNode>(mnubtnEnableRemote, remote => remote.Enable(fetch: false));
            RegisterClick<RemoteRepoNode>(mnubtnEnableRemoteAndFetch, remote => remote.Enable(fetch: true));
            RegisterClick<RemoteRepoNode>(mnubtnDisableRemote, remote => remote.Disable());

            // SubmoduleNode
            RegisterClick<SubmoduleNode>(mnubtnManageSubmodules, _ => _submoduleTree.ManageSubmodules(this));
            RegisterClick<SubmoduleNode>(mnubtnSynchronizeSubmodules, _ => _submoduleTree.SynchronizeSubmodules(this));
            RegisterClick<SubmoduleNode>(mnubtnOpenSubmodule, node => _submoduleTree.OpenSubmodule(this, node));
            RegisterClick<SubmoduleNode>(mnubtnOpenGESubmodule, node => _submoduleTree.OpenSubmoduleInGitExtensions(this, node));
            RegisterClick<SubmoduleNode>(mnubtnUpdateSubmodule, node => _submoduleTree.UpdateSubmodule(this, node));
            RegisterClick<SubmoduleNode>(mnubtnResetSubmodule, node => _submoduleTree.ResetSubmodule(this, node));
            RegisterClick<SubmoduleNode>(mnubtnStashSubmodule, node => _submoduleTree.StashSubmodule(this, node));
            RegisterClick<SubmoduleNode>(mnubtnCommitSubmodule, node => _submoduleTree.CommitSubmodule(this, node));

            // RemoteBranchNode
            RegisterClick<RemoteBranchNode>(mnubtnFetchOneBranch, remoteBranch => remoteBranch.Fetch());
            RegisterClick<RemoteBranchNode>(mnubtnPullFromRemoteBranch, remoteBranch => remoteBranch.FetchAndMerge());
            RegisterClick<RemoteBranchNode>(mnubtnRemoteBranchFetchAndCheckout, remoteBranch => remoteBranch.FetchAndCheckout());
            RegisterClick<RemoteBranchNode>(mnubtnFetchCreateBranch, remoteBranch => remoteBranch.FetchAndCreateBranch());
            RegisterClick<RemoteBranchNode>(mnubtnFetchRebase, remoteBranch => remoteBranch.FetchAndRebase());

            // BranchPathNode (folder)
            RegisterClick<BranchPathNode>(mnubtnDeleteAllBranches, branchPath => branchPath.DeleteAll());
            RegisterClick<BranchPathNode>(mnubtnCreateBranch, branchPath => branchPath.CreateBranch());

            // Expand / Collapse
            RegisterClick(mnubtnCollapse, () => GetMultiSelection().HavingChildren().Collapsible().ForEach(parent => parent.TreeViewNode.Collapse()));
            RegisterClick(mnubtnExpand, () => GetMultiSelection().HavingChildren().Expandable().ForEach(parent => parent.TreeViewNode.ExpandAll()));

            // Move up / down (for top level Trees)
            RegisterClick(mnubtnMoveUp, () => ReorderTreeNode(treeMain.SelectedNode, up: true));
            RegisterClick(mnubtnMoveDown, () => ReorderTreeNode(treeMain.SelectedNode, up: false));

            // Sort by / order
            _sortByContextMenuItem = new GitRefsSortByContextMenuItem(() => Refresh(new FilteredGitRefsProvider(UICommands.GitModule).GetRefs));
            _sortOrderContextMenuItem = new GitRefsSortOrderContextMenuItem(() => Refresh(new FilteredGitRefsProvider(UICommands.GitModule).GetRefs));
            menuMain.InsertItems(new ToolStripItem[] { new ToolStripSeparator(), _sortByContextMenuItem, _sortOrderContextMenuItem }, after: mnubtnMoveDown);
        }

        private void contextMenu_Opening(object sender, CancelEventArgs e)
        {
            if (sender is not ContextMenuStrip contextMenu)
            {
                return;
            }

            var selectedNodes = GetMultiSelection().ToArray();
            var hasSingleSelection = selectedNodes.Length <= 1;
            var selectedNode = treeMain.SelectedNode.Tag as NodeBase;

            copyContextMenuItem.Enable(hasSingleSelection && selectedNode is BaseBranchLeafNode branch && branch.Visible);
            filterForSelectedRefsMenuItem.Enable(selectedNodes.OfType<IGitRefActions>().Any());

            var selectedLocalBranch = selectedNode as LocalBranchNode;

            foreach (ToolStripItemWithKey item in _localBranchMenuItems)
            {
                bool visible = hasSingleSelection && selectedLocalBranch != null;
                item.Item.Visible = visible; // only display for single-selected branch

                /* Enabled items must also be visible; cancellation of menu opening below relies on it.
                 * Read from local variable because ToolStripItem.Visible will always returns false
                 * because the ContextMenuStrip as the visual parent is not Visible on Opening. */
                item.Item.Enabled = visible

                    // enable all items for non-current branches or only those applying to the current branch
                    && (selectedLocalBranch?.IsCurrent == false || LocalBranchMenuItems<LocalBranchNode>.CurrentBranchItemKeys.Contains(item.Key));
            }

            EnableRemoteBranchContextMenu(hasSingleSelection, selectedNode);
            EnableMenuItems(_tagNodeMenuItems, _ => hasSingleSelection && selectedNode is TagNode);
            EnableMenuItems(hasSingleSelection && selectedNode is RemoteBranchTree, mnuBtnManageRemotesFromRootNode, mnuBtnFetchAllRemotes, mnuBtnPruneAllRemotes);
            EnableRemoteRepoContextMenu(hasSingleSelection, selectedNode);
            EnableSubmoduleContextMenu(hasSingleSelection, selectedNode);
            EnableMenuItems(hasSingleSelection && selectedNode is BranchPathNode, mnubtnCreateBranch, mnubtnDeleteAllBranches);
            EnableExpandCollapseContextMenu(selectedNodes);
            EnableMoveTreeUpDownContexMenu(hasSingleSelection, selectedNode);
            EnableSortContextMenu(hasSingleSelection, selectedNode);

            if (hasSingleSelection && selectedLocalBranch?.Visible == true)
            {
                contextMenu.AddUserScripts(runScriptToolStripMenuItem, _scriptRunner.Execute);
            }
            else
            {
                contextMenu.RemoveUserScripts(runScriptToolStripMenuItem);
            }

            /* Cancel context menu opening if no items are Enabled.
             * This relies on that flag being set correctly on all menu items above. */
            e.Cancel = !contextMenu.Items.OfType<ToolStripMenuItem>().Any(i => i.Enabled);
        }

        private void contextMenu_Opened(object sender, EventArgs e)
            /* Waiting for ContextMenuStrip (as the visual parent of its menu items) to be visible to
             * toggle existing separators in between item groups as required depending on ToolStripItem.Visible .*/
            => (sender as ContextMenuStrip)?.ToggleSeparators();

        /// <inheritdoc />
        public TMenuItem CreateMenuItem<TMenuItem, TNode>(Action<TNode> onClick, TranslationString text, TranslationString toolTip, Bitmap? icon = null)
            where TMenuItem : ToolStripItem, new()
            where TNode : class, INode
        {
            TMenuItem result = new();
            result.Image = icon;
            result.Text = text.Text;
            result.ToolTipText = toolTip.Text;
            RegisterClick(result, onClick);
            return result;
        }
    }
}
