using System.Collections.Immutable;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Xml;

using static DiskPartitionInfo.DiskPartitionInfo;
using static Helper;

namespace hpesuperpower;

class Program
{
    const string StablePath = @"C:\Program Files\Google\Play Games\current";
    const string DevPath = @"C:\Program Files\Google\Play Games Developer Emulator\current";

    class Args
    {
        public bool Dev;
        public string Magisk = null!;
        public bool Restore;

        public bool Parse(string[] args)
        {
            for (int i = 0; i < args.Length; i++)
            {
                switch (args[i].ToLowerInvariant())
                {
                    case "-magisk":
                        if (i == args.Length - 1)
                        {
                            Console.WriteLine("<magisk_app> not specified");
                            break;
                        }
                        Magisk = Path.GetFullPath(args[++i]);
                        break;
                    case "-dev":
                        Dev = true;
                        break;
                    case "-restore":
                        Restore = true;
                        break;
                }
            }

            bool result = true;
            if (!Restore)
            {
                if (Magisk == null)
                    result = false;
                else if (!File.Exists(Magisk))
                {
                    Console.WriteLine($"{Magisk} doesn't exist.");
                    result = false;
                }
            }


            if (!result)
            {
                Console.WriteLine(
"""
Google Play Games Super Power. 
Author: ChsBuffer

Root Google Play Games Emulator: 
-magisk <magisk app> [-dev]
  -dev: Patch Play Games Developer Emulator
  <magisk_app>: path to Magisk app file


Restore all changes from backup:
-restore [-dev]
-dev: Restore on Play Games Developer Emulator

""");
            }
            return result;
        }
    }

    static string StartDirectory = null!;

    static void Main(string[] args)
    {
        StartDirectory = Environment.CurrentDirectory;

        bool isElevated;
        using (WindowsIdentity identity = WindowsIdentity.GetCurrent())
        {
            WindowsPrincipal principal = new WindowsPrincipal(identity);
            isElevated = principal.IsInRole(WindowsBuiltInRole.Administrator);
        }

        if (!isElevated)
        {
            Console.WriteLine("Please run this program from Elevated Command Prompt");
            return;
        }

        var arg = new Args();
        if (!arg.Parse(args))
        {
            return;
        }

        var installPath = arg.Dev ? DevPath : StablePath;

        if (!Directory.Exists(installPath))
        {
            Console.WriteLine("Google Play Games have not installed. Get it at https://play.google.com/googleplaygames or https://developer.android.com/games/playgames/emulator");
            return;
        }

        string aggregateImg = Path.Combine(installPath, @"emulator\avd\aggregate.img");
        string bios = Path.Combine(installPath, @"emulator\avd\bios.rom");
        string serviceConfig = Path.Combine(installPath, @"service\Service.exe.config");

        string stockBios = Path.Combine(installPath, @"emulator\avd\bios.rom.bak");
        string stockBootImg = Path.Combine(installPath, @"emulator\avd\boot_a.img");
        string stockServiceConfig = Path.Combine(installPath, @"service\Service.exe.config.bak");

        if (arg.Restore)
        {
            if (!File.Exists(stockBios))
            {
                Console.WriteLine("No backup founded.");
                return;
            }

            Console.WriteLine("\n\n############# Restore bios.rom");
            File.Copy(stockBios, bios, true);
            Console.WriteLine("\n\n############# Restore Service.exe.config");
            File.Copy(stockServiceConfig, serviceConfig, true);
            Console.WriteLine("\n\n############# Restore stock boot");
            FlashBoot(aggregateImg, stockBootImg);
            return;
        }

        if (!File.Exists(stockBios))
        {
            Console.WriteLine("\n\n############# Backup bios.rom");
            File.Copy(bios, stockBios);
        }

        Console.WriteLine("\n\n############# Patch bios.rom");
        PatchBios(bios);

        if (!File.Exists(stockServiceConfig))
        {
            Console.WriteLine("\n\n############# Backup Service.exe.config");
            File.Copy(serviceConfig, stockServiceConfig);
        }
        Console.WriteLine("\n\n############# Patch Service.exe.config");
        PatchKernelCmdline(serviceConfig);

        if (!File.Exists(stockBootImg))
            File.WriteAllBytes(stockBootImg, ExtractBoot(aggregateImg));

        Console.WriteLine("\n\n############# Patch boot.img");
        BootPatch(arg.Magisk, stockBootImg);

        Console.WriteLine("\n\n############# Flash patched boot img");
        FlashBoot(aggregateImg, "new-boot.img");

        Console.WriteLine("\n\n############# Cleanup");
        Environment.CurrentDirectory = StartDirectory;
        Directory.Delete(TempDirName, true);
    }

    static byte[] ExtractBoot(string diskPath)
    {
        const int LBS = 512; // Logical block size
        var gpt = ReadGpt().Primary().FromPath(diskPath);
        var part = gpt.Partitions.Single(x => x.Name == "boot_a");
        var offset = part.FirstLba * LBS;
        var size = (part.LastLba - part.FirstLba + 1) * LBS;

        var data = new byte[size];
        using var fs = File.OpenRead(diskPath);

        fs.Seek((long)offset, SeekOrigin.Begin);
        fs.ReadExactly(data);
        return data;
    }

    static void FlashBoot(string diskPath, string imgPath)
    {
        var gpt = ReadGpt().Primary().FromPath(diskPath);
        var offset = gpt.Partitions.Single(x => x.Name == "boot_a").FirstLba * 512;

        Console.WriteLine($"boot_a at {offset:X8}");

        using var fs = File.OpenWrite(diskPath);
        using var s = File.OpenRead(imgPath);
        fs.Seek((long)offset, SeekOrigin.Begin);
        s.CopyTo(fs);
    }


    static void ExtractResource(string key, string path)
    {
#if DEBUG
        File.Copy("..\\" + key, path);
#else
		var assembly = System.Reflection.Assembly.GetExecutingAssembly();
		using var stream = assembly
		  .GetManifestResourceStream($"{nameof(hpesuperpower)}.Resources.{key}")!;
		using var fs = File.OpenWrite(path);
		stream.CopyTo(fs);
#endif
    }

    const string TempDirName = "hpesuperpower_temp";
    static void BootPatch(string magiskApk, string boot_img)
    {
        /*
        ensure empty temp directory, 
        cd .\patch, 
        extract magiskboot.exe,
        copy boot_img,
        patch boot_img as new-boot.img
        */

        try
        {
            Directory.Delete(TempDirName, true);
        }
        catch { }

        var dir = Directory.CreateDirectory(TempDirName);

        Environment.CurrentDirectory = dir.FullName;
        File.Copy(boot_img, "boot.img");
        ExtractResource("magiskboot.exe", "magiskboot.exe");

        // remove no_install_unknown_sources_globally restriction
        ExtractResource("superpower.apk", "superpower.apk");
        ExtractResource("custom.rc", "custom.rc");

        using var magisk = File.OpenRead(magiskApk);
        var zip = new ZipArchive(magisk);
        zip.GetEntry("lib/x86_64/libmagisk.so")!.ExtractToFile("magisk");
        zip.GetEntry("assets/stub.apk")!.ExtractToFile("stub.apk");
        zip.GetEntry("lib/x86_64/libinit-ld.so")!.ExtractToFile("init-ld");
        zip.GetEntry("lib/x86_64/libmagiskinit.so")!.ExtractToFile("magiskinit");

        Run($"magiskboot.exe unpack boot.img").Z();
        string sha1;
        var status = Run("magiskboot.exe cpio ramdisk.cpio test");
        switch (status & 3)
        {
            case 0:
                Console.WriteLine("- Stock boot image detected");
                var sha1b = SHA1.HashData(File.ReadAllBytes(boot_img));
                sha1 = Convert.ToHexString(sha1b).ToLowerInvariant();
                break;
            case 1:
                Console.WriteLine("- Magisk patched boot image detected");
                Run("magiskboot.exe",
"""
cpio ramdisk.cpio 
"extract .backup/.magisk config.orig" 
"restore"
""".NoEOL()).Z();
                sha1 = File.ReadAllLines("config.orig").Single(x => x.StartsWith("SHA1=")).Substring("SHA1=".Length);
                break;
            default: // case 2
                Console.WriteLine("! Boot image patched by unsupported programs");
                return;
        }


        File.Copy("ramdisk.cpio", "ramdisk.cpio.orig");

        Run("magiskboot.exe compress=xz magisk magisk.xz").Z();
        Run("magiskboot.exe compress=xz stub.apk stub.xz").Z();
        Run("magiskboot.exe compress=xz init-ld init-ld.xz").Z();

        var config = new Dictionary<string, string>(){
{"KEEPVERITY","true"},
{"KEEPFORCEENCRYPT","true"},
{"RECOVERYMODE","false"},
{"PREINITDEVICE","metadata"},
{"SHA1",sha1},
};

        File.WriteAllText("config", string.Join("\n", config.Select(p => $"{p.Key}={p.Value}")));
        var steps = """
cpio ramdisk.cpio 
"add 0750 init magiskinit" 
"mkdir 0750 overlay.d" 
"mkdir 0750 overlay.d/sbin" 
"add 0644 overlay.d/sbin/magisk.xz magisk.xz" 
"add 0644 overlay.d/sbin/stub.xz stub.xz" 
"add 0644 overlay.d/sbin/init-ld.xz init-ld.xz" 
"patch" 
"backup ramdisk.cpio.orig" 
"mkdir 000 .backup" 
"add 000 .backup/.magisk config" 
"add 0644 overlay.d/custom.rc custom.rc" 
"add 0755 overlay.d/sbin/superpower.apk superpower.apk" 
""".NoEOL();

        Run("magiskboot.exe", steps, config).Z();
        Run("magiskboot.exe repack boot.img").Z();
    }

    static void PatchKernelCmdline(string serviceConfigPath)
    {
        // bypass kernel-space AVB.

        const string to = "androidboot.verifiedbootstate=orange ";
        var xml = new XmlDocument();
        xml.Load(serviceConfigPath);
        var value = xml.SelectNodes("/configuration/applicationSettings/Google.Hpe.Service.Properties.EmulatorSettings/setting[@name='EmulatorGuestParameters']/value")!.Item(0)!;

        var oldParam = value.InnerText;
        if (oldParam.Contains(to))
        {
            Console.WriteLine("Service.exe.config already modified, nothing to do.");
            return;
        }
        value.InnerText = to + value.InnerText;
        xml.Save(serviceConfigPath);
        Console.WriteLine("Service.exe.config modified.");
    }

    static void PatchBios(string path)
    {
        // disable secure boot

        var from = " verified_boot_android"u8;
        var to = "          boot_android"u8;

        var bios = File.ReadAllBytes(path);

        var off = bios.AsSpan().IndexOf(from);
        if (off == -1)
        {
            Console.WriteLine("hex not found, the bios might already been patched, nothing to do.");
            return;
        }
        Console.WriteLine($"bios patched at {off:X8}");
        to.CopyTo(bios.AsSpan(off));

        File.WriteAllBytes(path, bios);
    }
}
