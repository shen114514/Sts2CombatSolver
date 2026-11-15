using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using System.Windows.Forms;
using Microsoft.Win32;

internal static class Installer
{
    private const string ModName="Sts2CombatSolver";
    private const string PayloadHash="__PAYLOAD_HASH__";
    private const string Version="__VERSION__";
    private const string GameHash="62ECBE91093E1FA862DE4329AAC865209DCB297804CAF0F0520D92D8C0B4A9C3";
    private static readonly string[] Files={"Sts2CombatSolver.dll","Sts2CombatSolver.json","LICENSE"};
    [STAThread]
    private static int Main(string[] args)
    {
        bool quiet=args.Contains("--quiet");
        try
        {
            Application.EnableVisualStyles();
            string game=null;
            for(int i=0;i<args.Length-1;i++)if(args[i]=="--game-dir")game=args[i+1];
            if(game==null)game=FindGame();
            if(game==null&&!quiet)
            {
                using(var dialog=new FolderBrowserDialog{Description="请选择 Slay the Spire 2 游戏目录（包含 SlayTheSpire2.exe）",ShowNewFolderButton=false})
                    if(dialog.ShowDialog()==DialogResult.OK)game=dialog.SelectedPath;
            }
            if(game==null)throw new InvalidOperationException("没有找到游戏，请选择正确的 Steam 游戏目录。");
            string backup=Install(game);
            if(!quiet)MessageBox.Show("安装完成。\n\n进入战斗后自动计算并出牌。\n奖励、商店、事件与火堆可在设置中分别开启自动操作，默认关闭。\n\n原版本备份："+backup,"战斗求解器",MessageBoxButtons.OK,MessageBoxIcon.Information);
            return 0;
        }
        catch(Exception ex)
        {
            if(!quiet)MessageBox.Show(ex.Message,"安装未完成",MessageBoxButtons.OK,MessageBoxIcon.Error);
            else Console.Error.WriteLine(ex.Message);
            return 1;
        }
    }
    private static string Hash(byte[] bytes){using(var hash=SHA256.Create())return BitConverter.ToString(hash.ComputeHash(bytes)).Replace("-","");}
    private static string HashFile(string path){using(var hash=SHA256.Create())using(var file=File.OpenRead(path))return BitConverter.ToString(hash.ComputeHash(file)).Replace("-","");}
    private static void NoLink(string path)
    {
        for(var current=new DirectoryInfo(Path.GetFullPath(path));current!=null&&current.Parent!=null;current=current.Parent)
            if(current.Exists&&(current.Attributes&FileAttributes.ReparsePoint)!=0)throw new IOException("安装目标含目录链接，请改用普通 Steam 游戏目录："+current.FullName);
    }
    private static string Install(string selected)
    {
        string game=Path.GetFullPath(selected).TrimEnd(Path.DirectorySeparatorChar);
        NoLink(game);
        if(!File.Exists(Path.Combine(game,"SlayTheSpire2.exe")))throw new InvalidOperationException("所选目录不是 Windows 版 Slay the Spire 2。");
        string gameDll=Path.Combine(game,"data_sts2_windows_x86_64","sts2.dll");
        if(!File.Exists(gameDll)||HashFile(gameDll)!=GameHash)throw new InvalidOperationException("此安装包已验证的游戏版本为 v0.113.1。当前游戏程序集不匹配，未修改任何 Mod。");
        if(Process.GetProcessesByName("SlayTheSpire2").Length>0)throw new InvalidOperationException("请先关闭游戏再安装，存档不会被修改。");
        byte[] payload;
        using(var stream=Assembly.GetExecutingAssembly().GetManifestResourceStream("payload.zip"))
        using(var memory=new MemoryStream()){stream.CopyTo(memory);payload=memory.ToArray();}
        if(Hash(payload)!=PayloadHash)throw new InvalidDataException("安装包校验失败，请重新下载。");
        string mods=Path.Combine(game,"mods");NoLink(mods);
        string backup=Path.Combine(game,"mod-backups",ModName,DateTime.Now.ToString("yyyyMMdd-HHmmss")+"-"+Guid.NewGuid().ToString("N"));
        NoLink(Path.Combine(game,"mod-backups"));
        string stage=Path.Combine(backup,"staged");
        Directory.CreateDirectory(stage);
        using(var archive=new ZipArchive(new MemoryStream(payload),ZipArchiveMode.Read))
        {
            if(archive.Entries.Count!=Files.Length)throw new InvalidDataException("安装载荷文件数不正确。");
            foreach(string file in Files)
            {
                var entry=archive.GetEntry(file);
                if(entry==null||entry.Length==0)throw new InvalidDataException("安装载荷缺失："+file);
                using(var input=entry.Open())using(var output=File.Create(Path.Combine(stage,file)))input.CopyTo(output);
            }
        }
        Directory.CreateDirectory(mods);
        var moved=new List<string>();
        bool installed=false;
        string destination=Path.Combine(mods,ModName);
        try
        {
            foreach(string name in new[]{ModName,"Sts2Whale","CombatSolver"})
            {
                string old=Path.Combine(mods,name);NoLink(old);
                if(Directory.Exists(old)){Directory.Move(old,Path.Combine(backup,name));moved.Add(name);}
            }
            if(Process.GetProcessesByName("SlayTheSpire2").Length>0)throw new IOException("游戏在安装过程中启动，已恢复原 Mod。");
            Directory.Move(stage,destination);installed=true;
            File.WriteAllText(Path.Combine(backup,"installation.txt"),"Installed "+ModName+" "+Version+"\r\nGame: "+game+"\r\nFiles: "+string.Join(", ",Files)+"\r\nExisting unrelated mods, settings and saves were not changed.\r\n");
            return backup;
        }
        catch
        {
            if(installed)Directory.Move(destination,Path.Combine(backup,"failed-new-install"));
            foreach(string name in moved)Directory.Move(Path.Combine(backup,name),Path.Combine(mods,name));
            throw;
        }
    }
    private static string FindGame()
    {
        var steam=new List<string>();
        using(var key=Registry.CurrentUser.OpenSubKey(@"Software\Valve\Steam"))
            if(key!=null&&key.GetValue("SteamPath") is string)steam.Add((string)key.GetValue("SteamPath"));
        steam.Add(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),"Steam"));
        foreach(string path in steam.ToArray())
        {
            string vdf=Path.Combine(path,"steamapps","libraryfolders.vdf");
            if(!File.Exists(vdf))continue;
            foreach(Match match in Regex.Matches(File.ReadAllText(vdf),"\"path\"\\s+\"([^\"]+)\""))steam.Add(match.Groups[1].Value.Replace(@"\\",@"\"));
        }
        return steam.Distinct(StringComparer.OrdinalIgnoreCase).Select(path=>Path.Combine(path,"steamapps","common","Slay the Spire 2"))
            .FirstOrDefault(path=>File.Exists(Path.Combine(path,"SlayTheSpire2.exe")));
    }
}
