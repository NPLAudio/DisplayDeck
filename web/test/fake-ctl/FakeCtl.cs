// A stand-in for DisplayDeckCtl.exe used by the tests: records what it was asked to do, touches no hardware.
using System;
using System.IO;

public static class FakeCtl
{
    public static int Main(string[] a)
    {
        string log = Environment.GetEnvironmentVariable("FAKE_CTL_LOG");
        if (!string.IsNullOrEmpty(log)) File.AppendAllText(log, string.Join("|", a) + "\n");
        string g = a.Length > 0 ? a[0] : "", c = a.Length > 1 ? a[1] : "";
        if (g == "display" && c == "connected")
        {
            Console.Write("{\"displays\":[" +
              "{\"targetId\":4353,\"name\":\"MPG 491C OLED\",\"hwId\":\"MSI3FA8\",\"manufacturer\":26934,\"product\":16296,\"active\":true,\"modes\":[{\"width\":5120,\"height\":1440,\"refresh\":[60,120,144]},{\"width\":2560,\"height\":1440,\"refresh\":[60,144]}],\"preferred\":{\"width\":5120,\"height\":1440,\"refresh\":[60]}}," +
              "{\"targetId\":4355,\"name\":\"SONY TV\",\"hwId\":\"SNY0406\",\"manufacturer\":0,\"product\":0,\"active\":false,\"modes\":[],\"preferred\":{\"width\":3840,\"height\":2160,\"refresh\":[60]}}," +
              "{\"targetId\":4400,\"name\":\"New Monitor\",\"hwId\":\"DEL4000\",\"manufacturer\":0,\"product\":0,\"active\":false,\"modes\":[],\"preferred\":{\"width\":1920,\"height\":1080,\"refresh\":[60]}}]}");
            return 0;
        }
        if (g == "display" && c == "build") { File.Copy(a[2], a[3], true); Console.Write("{\"ok\":true,\"displays\":2}"); return 0; }
        if (g == "display" && c == "save") { string live = Environment.GetEnvironmentVariable("FAKE_LIVE_XML"); File.WriteAllText(a[2], string.IsNullOrEmpty(live) ? "LIVE-LAYOUT" : File.ReadAllText(live)); Console.Write("{\"ok\":true,\"displays\":2}"); return 0; }
        if (g == "display" && c == "apply")
        {
            string text = File.ReadAllText(a[2]);
            if (text.Contains("\"width\":99")) { Console.Write("{\"error\":\"Windows rejected the layout (error 87). Nothing was changed.\"}"); return 1; }
            Console.Write("{\"ok\":true,\"method\":\"full\",\"skipped\":[],\"matched\":[]}");
            return 0;
        }
        if (g == "display" && (c == "remember" || c == "undo")) { Console.Write("{\"ok\":true,\"skipped\":[]}"); return 0; }
        if (g == "audio" && c == "list")
        {
            Console.Write("{\"devices\":[{\"id\":\"Studio\\\\Device\\\\Main Out\\\\Render\",\"name\":\"Main Out 1-2\",\"adapter\":\"Studio Interface\",\"isDefault\":true,\"spatialGuid\":\"\",\"formats\":[]}]}");
            return 0;
        }
        Console.Write("{}");
        return 0;
    }
}
