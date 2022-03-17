﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Forms;
using GitUI.BranchTreePanel;
using GitUI.Hotkey;
using GitUI.Script;
using ResourceManager;

namespace GitUI.CommandsDialogs
{
    public static class UserScriptContextMenuExtensions
    {
        private const string ScriptNameSuffix = "_ownScript";

        private static readonly Lazy<IEnumerable<HotkeyCommand>> Hotkeys = new(()
            => HotkeySettingsManager.LoadHotkeys(FormSettings.HotkeySettingsName));

        /// <summary>
        ///  Adds user scripts to the <paramref name="contextMenu"/>, or under <paramref name="hostMenuItem"/>,
        ///  if scripts are not marked as <see cref="ScriptInfo.AddToRevisionGridContextMenu"/>.
        /// </summary>
        /// <param name="contextMenu">The context menu to add user scripts to.</param>
        /// <param name="hostMenuItem">The menu item user scripts not marked as <see cref="ScriptInfo.AddToRevisionGridContextMenu"/> are added to.</param>
        /// <param name="scriptInvoker">The handler that handles user script invocation.</param>
        public static void AddUserScripts(this ContextMenuStrip contextMenu, ToolStripMenuItem hostMenuItem, Action<string> scriptInvoker)
        {
            contextMenu = contextMenu ?? throw new ArgumentNullException(nameof(contextMenu));
            hostMenuItem = hostMenuItem ?? throw new ArgumentNullException(nameof(hostMenuItem));
            scriptInvoker = scriptInvoker ?? throw new ArgumentNullException(nameof(scriptInvoker));

            RemoveOwnScripts(contextMenu, hostMenuItem);

            var lastIndex = contextMenu.Items.Count;

            var scripts = ScriptManager.GetScripts()
                .Where(x => x.Enabled);

            foreach (var script in scripts)
            {
                ToolStripMenuItem item = new()
                {
                    Text = script.Name,
                    Name = script.Name + ScriptNameSuffix,
                    Image = script.GetIcon(),
                    ShortcutKeyDisplayString = Hotkeys.Value?.FirstOrDefault(h => h.Name == script.Name)?.KeyData.ToShortcutKeyDisplayString()
                };

                item.Click += (s, e) =>
                {
                    string? scriptKey = script.Name;

                    if (scriptKey is not null)
                    {
                        scriptInvoker(scriptKey);
                    }
                };

                if (script.AddToRevisionGridContextMenu)
                {
                    contextMenu.Items.Add(item);
                }
                else
                {
                    hostMenuItem.DropDown.Items.Add(item);
                    hostMenuItem.Toggle(true);
                }
            }

            if (lastIndex != contextMenu.Items.Count)
            {
                contextMenu.Items.Insert(lastIndex, new ToolStripSeparator());
            }
        }

        /// <summary>
        ///  Removes user scripts from the <paramref name="contextMenu"/>, or from <paramref name="hostMenuItem"/>,
        ///  if scripts are not marked as <see cref="ScriptInfo.AddToRevisionGridContextMenu"/>.
        /// </summary>
        /// <param name="contextMenu">The context menu to remove user scripts from.</param>
        /// <param name="hostMenuItem">The menu item from which to remove user scripts not marked as <see cref="ScriptInfo.AddToRevisionGridContextMenu"/>.</param>
        public static void RemoveUserScripts(this ContextMenuStrip contextMenu, ToolStripMenuItem hostMenuItem)
        {
            contextMenu = contextMenu ?? throw new ArgumentNullException(nameof(contextMenu));
            hostMenuItem = hostMenuItem ?? throw new ArgumentNullException(nameof(hostMenuItem));

            RemoveOwnScripts(contextMenu, hostMenuItem);
        }

        private static void RemoveOwnScripts(ContextMenuStrip contextMenu, ToolStripMenuItem hostMenuItem)
        {
            hostMenuItem.DropDown.Items.Clear();
            hostMenuItem.Toggle(false);

            var list = contextMenu.Items.Cast<ToolStripItem>()
                .Where(x => x.Name.EndsWith(ScriptNameSuffix))
                .ToList();

            foreach (var item in list)
            {
                contextMenu.Items.RemoveByKey(item.Name);
            }

            if (contextMenu.Items[contextMenu.Items.Count - 1] is ToolStripSeparator)
            {
                contextMenu.Items.RemoveAt(contextMenu.Items.Count - 1);
            }
        }
    }
}
