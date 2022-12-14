using System;
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
        // Component object definition
        public class Component : Category
        {
            public string URL { get; set; }
            public string Hash { get; set; }
            public long Size { get; set; }
            public string[] Depends { get; set; } = new string[] { };

            public Component(XmlNode node) : base(node)
            {
                // URL

                XmlNode rootElement = node.OwnerDocument.GetElementsByTagName("list")[0];

                if (rootElement.Attributes != null && rootElement.Attributes["url"] != null)
                {
                    URL = rootElement.Attributes["url"].Value + ID + ".zip";
                }
                else
                {
                    MessageBox.Show(
                        "An error occurred while parsing the component list XML. Please alert Flashpoint staff ASAP!\n\n" +
                        "Description: Root element does not contain URL attribute",
                        "Error", MessageBoxButtons.OK, MessageBoxIcon.Error
                    );

                    Environment.Exit(1);
                }

                // Hash

                string hash = GetAttribute(node, "hash", true);

                if (hash.Length == 8)
                {
                    Hash = hash;
                }
                else
                {
                    MessageBox.Show(
                        "An error occurred while parsing the component list XML. Please alert Flashpoint staff ASAP!\n\n" +
                        $"Description: Hash of component \"{Title}\" is invalid",
                        "Error", MessageBoxButtons.OK, MessageBoxIcon.Error
                    );

                    Environment.Exit(1);
                }

                // Size

                long size;

                if (long.TryParse(GetAttribute(node, "size", true), out size))
                {
                    Size = size;
                }
                else
                {
                    MessageBox.Show(
                        "An error occurred while parsing the component list XML. Please alert Flashpoint staff ASAP!\n\n" +
                        $"Description: Size of component \"{Title}\" is not a number",
                        "Error", MessageBoxButtons.OK, MessageBoxIcon.Error
                    );

                    Environment.Exit(1);
                }

                // Depends

                string depends = GetAttribute(node, "depends", false);

                if (depends.Length > 0) Depends = depends.Split(' ');
            }
        }

        // Category object definition
        public class Category
        {
            public string Title { get; set; }
            public string Description { get; set; }
            public string ID { get; set; }
            public bool Required { get; set; }

            public Category(XmlNode node)
            {
                // ID

                XmlNode workingNode = node.ParentNode;
                string id = GetAttribute(node, "id", true);

                while (workingNode != null && workingNode.Name != "list")
                {
                    if (workingNode.Attributes != null && workingNode.Name != "list")
                    {
                        id = $"{GetAttribute(workingNode, "id", true)}-{id}";
                    }

                    workingNode = workingNode.ParentNode;
                }

                ID = id;

                // Everything else

                Title = GetAttribute(node, "title", true);
                Description = GetAttribute(node, "description", true);
                Required = ID.StartsWith("required");
            }

            protected static string GetAttribute(XmlNode node, string attribute, bool throwError)
            {
                if (node.Attributes != null && node.Attributes[attribute] != null)
                {
                    return node.Attributes[attribute].Value;
                }
                else if (throwError)
                {
                    MessageBox.Show(
                        "An error occurred while parsing the component list XML. Please alert Flashpoint staff ASAP!\n\n" +
                        $"Description: Required {node.Name} attribute \"{attribute}\" was not found",
                        "Error", MessageBoxButtons.OK, MessageBoxIcon.Error
                    );

                    Environment.Exit(1);
                }

                return "";
            }
        }

        public static class FPM
        {
            // Pointer to main form
            public static Main Main { get => (Main)Application.OpenForms["Main"]; }
            // Internet location of component list XML
            public static string ListURL { get => "http://localhost/components.xml"; }
            // The parsed component list XML
            public static XmlDocument XmlTree { get; set; }

            // Pointer to destination path textbox
            public static string DestinationPath
            {
                get { return Main.DestinationPath.Text; }
                set { Main.DestinationPath.Text = value; }
            }
            // Pointer to source path textbox in Manage tab
            public static string SourcePath
            {
                get { return Main.SourcePath.Text; }
                set { Main.SourcePath.Text = value; }
            }
            // Pointer to source path textbox in Remove tab
            public static string SourcePath2
            {
                get { return Main.SourcePath2.Text; }
                set { Main.SourcePath2.Text = value; }
            }

            // Flag to control how operation window will function
            // 0 is for downloading Flashpoint
            // 1 is for adding/removing components
            // 2 is for updating components
            public static int OperateMode { get; set; } = 0;

            // Object for tracking numerous file size sums
            public static class SizeTracker
            {
                private static long toDownload;
                private static long modified;

                // Tracks total size of components available locally
                public static long Downloaded { get; set; }
                // Tracks total size of the pending Flashpoint download
                public static long ToDownload
                {
                    get => toDownload;
                    set
                    {
                        toDownload = value;
                        Main.DownloadSizeDisplay.Text = GetFormattedBytes(toDownload);
                    }
                }
                // Tracks size difference from checking/unchecking components in the manager tab
                public static long Modified
                {
                    get => modified;
                    set {
                        modified = value;
                        Main.ManagerSizeDisplay.Text = GetFormattedBytes(modified - Downloaded);
                    }
                }
            }

            // Object for tracking information about certain groups of components
            public static class ComponentTracker
            {
                // Information about components that are available locally
                public static List<Component> Downloaded { get; set; } = new List<Component>();
                // Information about components that are to be updated or added through the updater
                public static List<Component> ToUpdate   { get; set; } = new List<Component>();
            }

            // Performs an operation on every node in the specified TreeView
            public static void Iterate(TreeNodeCollection parent, Action<TreeNode> action)
            {
                foreach (TreeNode childNode in parent)
                {
                    action(childNode);

                    Iterate(childNode.Nodes, action);
                }
            }

            // Calls the AddNodeToList method on every child of the specified XML node
            public static void RecursiveAddToList(XmlNode sourceNode, TreeNodeCollection destNode, bool setCheckState)
            {
                foreach (XmlNode node in sourceNode.ChildNodes)
                {
                    var listNode = AddNodeToList(node, destNode, setCheckState);

                    RecursiveAddToList(node, listNode.Nodes, setCheckState);
                }
            }

            // Formats an XML node as a TreeView node and adds it to the specified TreeView 
            public static TreeNode AddNodeToList(XmlNode child, TreeNodeCollection parent, bool setCheckState)
            {
                TreeNode listNode = new TreeNode();

                // Add properties to TreeNode based on the XML element
                // (I can use the dynamic type to prevent redundancy, but I noticed it makes the application load significantly slower)
                if (child.Name == "component")
                {
                    Component component = new Component(child);

                    listNode.Text = component.Title;

                    if (component.ID.StartsWith("required"))
                    {
                        listNode.ForeColor = Color.FromArgb(255, 96, 96, 96);
                    }

                    listNode.Tag = component;
                }
                else if (child.Name == "category")
                {
                    Category category = new Category(child);

                    listNode.Text = category.Title;

                    if (category.ID.StartsWith("required"))
                    {
                        listNode.ForeColor = Color.FromArgb(255, 96, 96, 96);
                    }

                    listNode.Tag = category;
                }

                parent.Add(listNode);

                // Initialize checkbox
                // (the Checked attribute needs to be explicitly set or else the checkbox won't appear)
                listNode.Checked = setCheckState && child.Name == "component";

                return listNode;
            }

            // Refreshes tracker objects with up-to-date information
            public static void SyncManager(bool setCheckState = false)
            {
                ComponentTracker.Downloaded.Clear();

                Iterate(Main.ComponentList2.Nodes, node =>
                {
                    if (node.Tag.GetType().ToString().EndsWith("Component"))
                    {
                        Component component = node.Tag as Component;
                        string infoPath = Path.Combine(SourcePath, "Components", $"{component.ID}.txt");

                        if (File.Exists(infoPath)) ComponentTracker.Downloaded.Add(component);

                        if (setCheckState) node.Checked = File.Exists(infoPath);
                    }
                });

                SizeTracker.Downloaded = GetTotalSize(Main.ComponentList2);
                SizeTracker.Modified   = GetTotalSize(Main.ComponentList2);
            }

            // Deletes a file as well as any directories made empty by its deletion
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

            // Checks if specified Flashpoint destination path is valid, and optionally updates its respective textbox
            public static bool VerifyDestinationPath(string path, bool updateText)
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

            // Checks if specified Flashpoint source path is valid, and optionally updates one of the textboxes
            public static bool VerifySourcePath(string path, int textBox = 0)
            {
                bool isFlashpoint = false;

                Iterate(Main.ComponentList.Nodes, node =>
                {
                    if (node.Tag.GetType().ToString().EndsWith("Component"))
                    {
                        Component component = node.Tag as Component;
                        string infoPath = Path.Combine(path, "Components", $"{component.ID}.txt");

                        if (File.Exists(infoPath))
                        {
                            isFlashpoint = true;

                            return;
                        }
                    }
                });

                if (isFlashpoint)
                {
                    if (textBox == 1) SourcePath  = path;
                    if (textBox == 2) SourcePath2 = path;

                    return true;
                }

                MessageBox.Show($"Flashpoint was not found in this directory!", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);

                return false;
            }

            // Checks if any dependencies were not marked for download by the user, and marks them accordingly
            public static bool CheckDependencies(TreeView sourceTree)
            {
                List<string> requiredDepends = new List<string>();
                List<string> persistDepends  = new List<string>();
                List<string> missingDepends  = new List<string>();

                // First, fill out a list of dependencies
                Iterate(sourceTree.Nodes, node =>
                {
                    if (node.Checked && node.Tag.GetType().ToString().EndsWith("Component"))
                    {
                        Component component = node.Tag as Component;
                        string infoPath = Path.Combine(SourcePath, "Components", $"{component.ID}.txt");

                        if (sourceTree.Name == "ComponentList2" && File.Exists(infoPath))
                        {
                            requiredDepends.AddRange(File.ReadLines(infoPath).First().Split(' ').Skip(2).ToArray());
                        }
                        else
                        {
                            requiredDepends.AddRange((node.Tag as Component).Depends);
                        }
                    }
                });

                // Then make sure they're all marked for installation 
                Iterate(sourceTree.Nodes, node =>
                {
                    if (node.Tag.GetType().ToString().EndsWith("Component"))
                    {
                        Component component = node.Tag as Component;

                        if (requiredDepends.Any(depend => depend == component.ID) && !node.Checked)
                        {
                            node.Checked = true;

                            if (ComponentTracker.Downloaded.Any(depend => depend.ID == component.ID))
                            {
                                persistDepends.Add(component.Title);
                            }
                            else
                            {
                                missingDepends.Add(component.Title);
                            }
                        }
                    }
                });

                if (persistDepends.Count > 0)
                {
                    MessageBox.Show(
                        "The following components cannot be removed because one or more components depend on them:\n\n" +
                        string.Join(", ", persistDepends), "Error", MessageBoxButtons.OK, MessageBoxIcon.Error
                    );

                    return false;
                }

                if (missingDepends.Count > 0)
                {
                    MessageBox.Show(
                        "The following dependencies will also be installed:\n\n" +
                        string.Join(", ", missingDepends) + "\n\nClick the OK button to proceed.",
                        "Notice", MessageBoxButtons.OK, MessageBoxIcon.Information
                    );
                }

                return true;
            }

            // Gets total size in bytes of all checked components in the specified TreeView
            public static long GetTotalSize(TreeView sourceTree)
            {
                long size = 0;

                Iterate(sourceTree.Nodes, node =>
                {
                    if (node.Checked && node.Tag.GetType().ToString().EndsWith("Component"))
                    {
                        Component component = node.Tag as Component;
                        string infoPath = Path.Combine(SourcePath, "Components", $"{component.ID}.txt");

                        size += sourceTree.Name == "ComponentList2" && File.Exists(infoPath)
                            ? long.Parse(File.ReadLines(infoPath).First().Split(' ')[1])
                            : component.Size;
                    }
                });

                return size;
            }

            // Formats bytes as a human-readable string
            public static string GetFormattedBytes(long bytes)
            {
                if (bytes >= 1000000000)
                {
                    return (Math.Truncate((double)bytes / 100000000) / 10).ToString("N1") + "GB";
                }
                else if (bytes >= 1000000)
                {
                    return (Math.Truncate((double)bytes / 100000) / 10).ToString("N1") + "MB";
                }
                else
                {
                    return (Math.Truncate((double)bytes / 100) / 10).ToString("N1") + "KB";
                }
            }
        }
    }
}