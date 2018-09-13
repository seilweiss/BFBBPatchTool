using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using System.Text;

namespace BFBBPatchTool
{
    class Program
    {
        public enum Option
        {
            None = -1,
            Install = 0,
            Create = 1,
            Close = 2
        }

        public enum PatchOption
        {
            None = -1,
            Confirm = 0,
            Cancel = 1
        }

        [STAThread]
        static void Main(string[] args)
        {
            Console.WriteLine("BFBB Patch Tool v0.4 by Seil");

            if (args.Length == 0)
            {
                Option option = Option.None;

                while (option != Option.Close)
                {
                    Console.WriteLine();
                    Console.WriteLine("Press 0 to install a patch.");
                    Console.WriteLine("Press 1 to create a new patch.");
                    Console.WriteLine("Press 2 to quit.");

                    option = ReadOption();

                    if (option == Option.Create)
                    {
                        FolderBrowserDialog folderDialog = new FolderBrowserDialog();
                        folderDialog.Description = "Select the root folder of your UN-MODDED game.";

                        if (folderDialog.ShowDialog() != DialogResult.OK) continue;

                        string originalRoot = folderDialog.SelectedPath;
                        Console.WriteLine("Original game: " + originalRoot);

                        folderDialog.Description = "Select the root folder of your MODDED game.";

                        if (folderDialog.ShowDialog() != DialogResult.OK) continue;

                        string moddedRoot = folderDialog.SelectedPath;
                        Console.WriteLine("Modded game: " + moddedRoot);

                        SaveFileDialog fileDialog = new SaveFileDialog
                        {
                            Title = "Save patch file",
                            FileName = "mod.patch",
                            Filter = "Patch file (*.patch)|*.patch|All files|*.*"
                        };

                        if (fileDialog.ShowDialog() != DialogResult.OK) continue;

                        string patchFile = fileDialog.FileName;
                        Console.WriteLine("Patch file: " + patchFile);

                        Patch patch = new Patch(originalRoot, moddedRoot);
                        patch.Write(patchFile);

                        Console.WriteLine("Patch file saved to " + patchFile);
                    }
                    else if (option == Option.Install)
                    {
                        FolderBrowserDialog folderDialog = new FolderBrowserDialog
                        {
                            Description = "Select the root folder of your game."
                        };

                        if (folderDialog.ShowDialog() != DialogResult.OK) continue;

                        string root = folderDialog.SelectedPath;
                        Console.WriteLine("Game root: " + root);

                        OpenFileDialog openDialog = new OpenFileDialog
                        {
                            Title = "Open patch file",
                            Filter = "Patch file (*.patch)|*.patch|All files|*.*"
                        };

                        if (openDialog.ShowDialog() != DialogResult.OK) continue;

                        string patchFile = openDialog.FileName;
                        Console.WriteLine("Patch file: " + patchFile);

                        Patch patch = new Patch(patchFile);

                        string uninstallFile = null;
                        if (!patch.isUninstall)
                        {
                            SaveFileDialog saveDialog = new SaveFileDialog
                            {
                                Title = "Save uninstall patch file",
                                Filter = "Patch file (*.patch)|*.patch|All files|*.*"
                            };

                            if (saveDialog.ShowDialog() == DialogResult.OK)
                            {
                                uninstallFile = saveDialog.FileName;
                                Console.WriteLine("Uninstall patch file: " + uninstallFile);
                            }
                        }
                        
                        Console.WriteLine();

                        patch.Print();
                        
                        PatchOption patchOption = PatchOption.None;

                        while (patchOption != PatchOption.Confirm)
                        {
                            Console.WriteLine();
                            Console.WriteLine("Do you want to commit this patch?");
                            Console.WriteLine();
                            Console.WriteLine("Press 0 to patch your game.");
                            Console.WriteLine("Press 1 to cancel.");

                            patchOption = ReadPatchOption();

                            if (patchOption == PatchOption.Confirm)
                            {
                                Patch uninstall = patch.Commit(root);

                                if (uninstallFile != null)
                                {
                                    uninstall.Write(uninstallFile);

                                    Console.WriteLine("Uninstaller patch saved to " + uninstallFile);
                                    Console.WriteLine("Run this patch to restore your game to its original version.");
                                    Console.WriteLine();
                                }
                            }
                            else if (patchOption == PatchOption.Cancel)
                                break;
                        }
                    }
                }
            }
        }

        static Option ReadOption()
        {
            Option option;

            try
            {
                option = (Option)Convert.ToInt32(Console.ReadLine());
            }
            catch
            {
                option = Option.None;
            }

            return option;
        }

        static PatchOption ReadPatchOption()
        {
            PatchOption option;

            try
            {
                option = (PatchOption)Convert.ToInt32(Console.ReadLine());
            }
            catch
            {
                option = PatchOption.None;
            }

            return option;
        }
    }
}