﻿using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using System.Xml;

namespace FlashpointInstaller
{
    namespace Common
    {
        public static class FPM
        {
            public static Main Main { get { return (Main)Application.OpenForms["Main"]; } }
            public static XmlDocument XmlTree { get; set; }
            public static string ListURL { get; set; }

            public static string DestinationPath
            {
                get { return Main.DestinationPath.Text; }
                set { Main.DestinationPath.Text = value; }
            }
            public static string SourcePath
            {
                get { return Main.SourcePath.Text; }
                set { Main.SourcePath.Text = value; }
            }

            public static int DownloadMode { get; set; } = 0;

            public static class SizeTracker
            {
                private static long toDownload;
                private static long modified;

                public static long Downloaded { get; set; }
                public static long ToDownload
                {
                    get => toDownload;
                    set
                    {
                        toDownload = value;
                        Main.DownloadSizeDisplay.Text = GetFormattedBytes(toDownload);
                    }
                }
                public static long Modified
                {
                    get => modified;
                    set {
                        modified = value;
                        Main.ManagerSizeDisplay.Text = GetFormattedBytes(modified - Downloaded);
                    }
                }
            }

            public static class ComponentTracker
            {
                public static List<Dictionary<string, string>> ToDownload { get; set; } = new List<Dictionary<string, string>>();
                public static List<Dictionary<string, string>> Downloaded { get; set; } = new List<Dictionary<string, string>>();
                public static List<Dictionary<string, string>> ToAdd      { get; set; } = new List<Dictionary<string, string>>();
                public static List<Dictionary<string, string>> ToRemove   { get; set; } = new List<Dictionary<string, string>>();
                public static List<Dictionary<string, string>> ToUpdate   { get; set; } = new List<Dictionary<string, string>>();
            }

            public static void Iterate(TreeNodeCollection parent, Action<TreeNode> action)
            {
                foreach (TreeNode childNode in parent)
                {
                    action(childNode);

                    Iterate(childNode.Nodes, action);
                }
            }

            public static void RecursiveAddToList(XmlNode sourceNode, TreeNodeCollection destNode, bool setCheckState)
            {
                foreach (XmlNode node in sourceNode.ChildNodes)
                {
                    var listNode = AddNodeToList(node, destNode, setCheckState);

                    RecursiveAddToList(node, listNode.Nodes, setCheckState);
                }
            }

            public static TreeNode AddNodeToList(XmlNode child, TreeNodeCollection parent, bool setCheckState)
            {
                var listNode = parent.Add(child.Attributes["title"].Value);
                listNode.Tag = new Dictionary<string, string>
                {
                    { "title", child.Attributes["title"].Value },
                    { "url", GetComponentURL(child) },
                    { "path", GetComponentPath(child) },
                    { "description", child.Attributes["description"].Value },
                    { "type", child.Name },
                    { "disabled", "false" }
                };

                if ((listNode.Tag as Dictionary<string, string>)["type"] == "component")
                {
                    (listNode.Tag as Dictionary<string, string>).Add("size", child.Attributes["size"].Value);
                    (listNode.Tag as Dictionary<string, string>).Add("hash", child.Attributes["hash"].Value);
                }

                listNode.Checked = (setCheckState && child.Attributes["checked"] != null)
                    ? bool.Parse(child.Attributes["checked"].Value) : listNode.Checked;

                if ((child.ParentNode.Name == "category" && child.ParentNode.Attributes["required"].Value == "true")
                 || (child.Name == "category" && child.Attributes["required"].Value == "true"))
                {
                    listNode.ForeColor = Color.FromArgb(255, 96, 96, 96);
                    (listNode.Tag as Dictionary<string, string>)["disabled"] = "true";
                }

                return listNode;
            }

            public static void SyncManager(bool setCheckState = false)
            {
                ComponentTracker.Downloaded.Clear();

                Iterate(Main.ComponentList2.Nodes, node =>
                {
                    var attributes = node.Tag as Dictionary<string, string>;

                    if (attributes["type"] == "component")
                    {
                        string infoPath = Path.Combine(SourcePath, "Components", attributes["path"], $"{attributes["title"]}.txt");

                        if (File.Exists(infoPath)) ComponentTracker.Downloaded.Add(attributes);

                        if (setCheckState) node.Checked = File.Exists(infoPath) ? true : false;
                    }
                });

                SizeTracker.Downloaded  = GetEstimatedSize(Main.ComponentList2.Nodes);
                SizeTracker.Modified    = GetEstimatedSize(Main.ComponentList2.Nodes);
            }

            public static void DeleteFileAndDirectories(string file)
            {
                if (File.Exists(file)) File.Delete(file);

                string folder = Path.GetDirectoryName(file);

                while (folder != SourcePath)
                {
                    if (Directory.Exists(folder) && !Directory.EnumerateFileSystemEntries(folder).Any())
                    {
                        Directory.Delete(folder, false);
                    }
                    else break;

                    folder = Directory.GetParent(folder).ToString();
                }
            }

            public static bool SetDownloadPath(string path, bool updateText)
            {
                string errorPath;

                if (path.StartsWith(Environment.ExpandEnvironmentVariables("%ProgramW6432%"))
                 || path.StartsWith(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86)))
                {
                    errorPath = "Program Files";
                }
                else if (path.StartsWith(Path.GetTempPath().Remove(Path.GetTempPath().Length - 1)))
                {
                    errorPath = "Temporary Files";
                }
                else if (path.StartsWith(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "OneDrive")))
                {
                    errorPath = "OneDrive";
                }
                else
                {
                    if (updateText) DestinationPath = Path.Combine(path, "Flashpoint");

                    return true;
                }

                MessageBox.Show(
                    $"Flashpoint cannot be installed to the {errorPath} directory! Choose a different folder.",
                    "Error", MessageBoxButtons.OK, MessageBoxIcon.Error
                );

                return false;
            }

            public static bool SetFlashpointPath(string path, bool updateText)
            {
                bool isFlashpoint = false;

                Iterate(Main.ComponentList2.Nodes, node =>
                {
                    var attributes = node.Tag as Dictionary<string, string>;
                    string infoPath = Path.Combine(path, "Components", attributes["path"], $"{attributes["title"]}.txt");

                    if (File.Exists(infoPath))
                    {
                        isFlashpoint = true;

                        return;
                    }
                });

                if (isFlashpoint)
                {
                    if (updateText) SourcePath = path;

                    return true;
                }

                MessageBox.Show($"Flashpoint was not found in this directory!", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);

                return false;
            }

            public static long GetEstimatedSize(TreeNodeCollection sourceNodes)
            {
                long size = 0;

                Iterate(sourceNodes, node =>
                {
                    var attributes = node.Tag as Dictionary<string, string>;

                    if (node.Checked && attributes["type"] == "component")
                    {
                        size += long.Parse(attributes["size"]);
                    }
                });

                return size;
            }

            public static string GetComponentURL(XmlNode node)
            {
                string path = node.Attributes["url"].Value;

                node = node.ParentNode;

                while (node != null)
                {
                    if (node.Attributes != null)
                    {
                        path = $"{node.Attributes["url"].Value}/{path}";
                    }

                    node = node.ParentNode;
                }

                return path;
            }

            public static string GetComponentPath(XmlNode node)
            {
                string path = "";

                node = node.ParentNode;

                while (node != null && node.Name != "list")
                {
                    if (node.Attributes != null)
                    {
                        path = $"{node.Attributes["url"].Value}\\{path}";
                    }

                    node = node.ParentNode;
                }

                return path;
            }

            public static string GetFormattedBytes(long bytes)
            {
                if (bytes >= 1000000000000)
                {
                    return (Math.Truncate((double)bytes / 100000000000) / 10).ToString("N1") + "TB";
                }
                else if (bytes >= 1000000000)
                {
                    return (Math.Truncate((double)bytes / 100000000) / 10).ToString("N1") + "GB";
                }
                else
                {
                    return (Math.Truncate((double)bytes / 100000) / 10).ToString("N1") + "MB";
                }
            }
        }
    }
}