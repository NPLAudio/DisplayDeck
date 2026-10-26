// `DisplayDeckCtl selftest`: exercises the pure logic (profile XML, adapter remapping) with no hardware access.
using System;
using System.Collections.Generic;

namespace DisplayDeck.Ctl
{
    public static class SelfTest
    {
        // Two displays, saved against adapter 1000 (the live adapter id will differ).
        const string Sample =
            "<?xml version=\"1.0\" encoding=\"utf-8\"?><displaySettings><pathInfoArray>" +
            "<DisplayConfigPathInfo><sourceInfo><adapterId><LowPart>1000</LowPart><HighPart>0</HighPart></adapterId><id>0</id><modeInfoIdx>1</modeInfoIdx><statusFlags>InUse</statusFlags></sourceInfo>" +
            "<targetInfo><adapterId><LowPart>1000</LowPart><HighPart>0</HighPart></adapterId><id>4353</id><modeInfoIdx>0</modeInfoIdx><outputTechnology>DisplayportExternal</outputTechnology><rotation>Identity</rotation><scaling>Identity</scaling><refreshRate><numerator>144</numerator><denominator>1</denominator></refreshRate><scanLineOrdering>Progressive</scanLineOrdering><targetAvailable>true</targetAvailable><statusFlags>InUse</statusFlags></targetInfo><flags>1</flags></DisplayConfigPathInfo>" +
            "<DisplayConfigPathInfo><sourceInfo><adapterId><LowPart>1000</LowPart><HighPart>0</HighPart></adapterId><id>1</id><modeInfoIdx>3</modeInfoIdx><statusFlags>InUse</statusFlags></sourceInfo>" +
            "<targetInfo><adapterId><LowPart>1000</LowPart><HighPart>0</HighPart></adapterId><id>4355</id><modeInfoIdx>2</modeInfoIdx><outputTechnology>Hdmi</outputTechnology><rotation>Identity</rotation><scaling>Identity</scaling><refreshRate><numerator>60</numerator><denominator>1</denominator></refreshRate><scanLineOrdering>Progressive</scanLineOrdering><targetAvailable>true</targetAvailable><statusFlags>InUse</statusFlags></targetInfo><flags>1</flags></DisplayConfigPathInfo>" +
            "</pathInfoArray><modeInfoArray>" +
            "<modeInfo><id>4353</id><LUID><LowPart>1000</LowPart><HighPart>0</HighPart></LUID><DisplayConfigModeInfoType>Target</DisplayConfigModeInfoType><DisplayConfigTargetMode><targetVideoSignalInfo><pixelRate>1160160000</pixelRate><hSyncFreq><numerator>1160160000</numerator><denominator>5440</denominator></hSyncFreq><vSyncFreq><numerator>144</numerator><denominator>1</denominator></vSyncFreq><activeSize><cx>5120</cx><cy>1440</cy></activeSize><totalSize><cx>5440</cx><cy>1481</cy></totalSize><videoStandard>Other</videoStandard><ScanLineOrdering>Progressive</ScanLineOrdering></targetVideoSignalInfo></DisplayConfigTargetMode></modeInfo>" +
            "<modeInfo><id>0</id><LUID><LowPart>1000</LowPart><HighPart>0</HighPart></LUID><DisplayConfigModeInfoType>Source</DisplayConfigModeInfoType><DisplayConfigSourceMode><width>5120</width><height>1440</height><pixelFormat>Pixelformat32Bpp</pixelFormat><position><x>0</x><y>0</y></position></DisplayConfigSourceMode></modeInfo>" +
            "<modeInfo><id>4355</id><LUID><LowPart>1000</LowPart><HighPart>0</HighPart></LUID><DisplayConfigModeInfoType>Target</DisplayConfigModeInfoType><DisplayConfigTargetMode><targetVideoSignalInfo><pixelRate>594000000</pixelRate><hSyncFreq><numerator>594000000</numerator><denominator>4400</denominator></hSyncFreq><vSyncFreq><numerator>60</numerator><denominator>1</denominator></vSyncFreq><activeSize><cx>3840</cx><cy>2160</cy></activeSize><totalSize><cx>4400</cx><cy>2250</cy></totalSize><videoStandard>Eia861</videoStandard><ScanLineOrdering>Progressive</ScanLineOrdering></targetVideoSignalInfo></DisplayConfigTargetMode></modeInfo>" +
            "<modeInfo><id>1</id><LUID><LowPart>1000</LowPart><HighPart>0</HighPart></LUID><DisplayConfigModeInfoType>Source</DisplayConfigModeInfoType><DisplayConfigSourceMode><width>3840</width><height>2160</height><pixelFormat>Pixelformat32Bpp</pixelFormat><position><x>5120</x><y>0</y></position></DisplayConfigSourceMode></modeInfo>" +
            "</modeInfoArray><additionalInfo>" +
            "<MonitorAdditionalInfo><manufactureId>26934</manufactureId><productCodeId>16296</productCodeId><valid>true</valid><monitorDevicePath>{DEV1}</monitorDevicePath><monitorFriendlyDevice>{NAME1}</monitorFriendlyDevice></MonitorAdditionalInfo>" +
            "<MonitorAdditionalInfo><manufactureId>0</manufactureId><productCodeId>0</productCodeId><valid>false</valid><monitorDevicePath /><monitorFriendlyDevice /></MonitorAdditionalInfo>" +
            "</additionalInfo></displaySettings>";

        static string Sample2()
        {
            return Sample
                .Replace("{DEV1}", ProfileFile.Encode("\\\\?\\DISPLAY#MSI3FA8#5&00000000&1&UID4353#{e6f07b5f-ee97-4a90-b076-33f57bf4eaa7}"))
                .Replace("{NAME1}", ProfileFile.Encode("MPG 491C OLED"));
        }

        static int passed;
        static List<string> failed = new List<string>();

        static void Check(bool ok, string what)
        {
            if (ok) passed++; else failed.Add(what);
        }

        public static Dictionary<string, object> Run()
        {
            Profile p = ProfileFile.Parse(Sample2());
            Check(p.Paths.Count == 2, "parses two paths");
            Check(p.Modes.Count == 4, "parses four modes");
            Check(p.Paths[0].targetInfo.id == 4353 && p.Paths[1].targetInfo.id == 4355, "target ids");
            Check(p.Paths[0].targetInfo.refreshRate.Numerator == 144, "refresh rate");
            Check(p.Paths[0].targetInfo.outputTechnology == (uint)OutputTechnology.DisplayportExternal, "output technology name");
            Check(p.Modes[1].sourceMode.width == 5120 && p.Modes[3].sourceMode.positionX == 5120, "source modes");
            Check(p.Modes[2].targetMode.videoStandard == (uint)VideoStandard.Eia861, "video standard name");
            Check(p.Monitors.Count == 1 && p.Monitors[0].FriendlyName == "MPG 491C OLED", "monitor name decode");
            Check(p.Monitors[0].TargetId() == 4353, "target id from device path");

            // round trip
            Profile q = ProfileFile.Parse(ProfileFile.Write(p));
            Check(q.Paths.Count == 2 && q.Modes.Count == 4 && q.Monitors.Count == 1, "round trip counts");
            Check(q.Paths[1].targetInfo.id == 4355 && q.Paths[1].targetInfo.refreshRate.Numerator == 60, "round trip path fields");
            Check(q.Modes[3].sourceMode.positionX == 5120 && q.Modes[0].targetMode.totalCx == 5440, "round trip mode fields");
            Check(q.Monitors[0].DevicePath == p.Monitors[0].DevicePath, "round trip device path");

            // tolerance
            Check(Throws(delegate { ProfileFile.Parse("not xml"); }), "rejects non-xml");
            Check(Throws(delegate { ProfileFile.Parse("<a/>"); }), "rejects xml without paths");
            Profile extra = ProfileFile.Parse(Sample2().Replace("<flags>1</flags>", "<flags>1</flags><someNewField>x</someNewField>"));
            Check(extra.Paths.Count == 2, "ignores unknown elements");

            // remapping to a different adapter, one display missing
            PathInfo[] current = new PathInfo[1];
            current[0] = p.Paths[0];
            current[0].targetInfo.adapterId.LowPart = 2222;      // adapter id changed after a reboot
            current[0].sourceInfo.adapterId.LowPart = 2222;
            List<string> skipped = new List<string>();
            List<MatchedDisplay> m = DisplayOps.Match(p, current, skipped);
            Check(m.Count == 1 && m[0].TargetId == 4353, "matches the connected display");
            Check(skipped.Count == 1 && skipped[0].Contains("not connected"), "reports the missing display");

            PathInfo[] paths; ModeInfo[] modes;
            DisplayOps.Build(p, m, out paths, out modes);
            Check(paths.Length == 1 && modes.Length == 2, "builds one path, two modes");
            Check(paths[0].targetInfo.adapterId.LowPart == 2222 && modes[0].adapterId.LowPart == 2222 && modes[1].adapterId.LowPart == 2222, "uses the current adapter id");
            Check(paths[0].sourceInfo.modeInfoIdx == 0 && paths[0].targetInfo.modeInfoIdx == 1, "mode indices point into the new array");
            Check(modes[0].infoType == DisplayNative.MODE_SOURCE && modes[0].sourceMode.width == 5120 && modes[1].targetMode.activeCx == 5120, "carries saved resolution");
            Check(paths[0].targetInfo.refreshRate.Numerator == 144, "carries saved refresh rate");

            // monitor identity without a device path (Windows sometimes omits it): EDID ids -> hardware id
            Check(ProfileFile.EdidHwId(26934, 16296) == "MSI3FA8", "EDID ids give MSI3FA8");
            Check(ProfileFile.EdidHwId(11596, 3996) == "SAM0F9C", "EDID ids give SAM0F9C");
            string synth = ProfileFile.SynthesizeDevicePath(4353, 26934, 16296);
            MonitorInfo sm2 = new MonitorInfo(); sm2.DevicePath = synth;
            Check(sm2.TargetId() == 4353 && synth.Contains("DISPLAY#MSI3FA8#"), "synthesized path carries the target id");
            // an older file with empty device paths: entries are matched to paths by order
            Profile bare = ProfileFile.Parse(Sample2().Replace(ProfileFile.Encode("\\\\?\\DISPLAY#MSI3FA8#5&00000000&1&UID4353#{e6f07b5f-ee97-4a90-b076-33f57bf4eaa7}"), ""));
            Check(bare.Monitors.Count == 1 && bare.Monitors[0].TargetId() == null, "a file with an empty device path has no target id");
            PathInfo[] cur1 = new PathInfo[1]; cur1[0] = bare.Paths[0];
            List<string> sk1 = new List<string>();
            Check(DisplayOps.Match(bare, cur1, sk1).Count == 1, "still matches the display by connector id");

            // nothing connected -> no matches
            List<string> none = new List<string>();
            Check(DisplayOps.Match(p, new PathInfo[0], none).Count == 0 && none.Count == 2, "no matches when nothing is connected");

            // audio format parsing and name resolution
            Guid atmos = new Guid("a289735d-fa3e-4e35-9d7d-b6f896acb2e7");
            byte[] blob = FormatRecord("Dolby Atmos for Home Theater", atmos, true);
            SpatialFormat f = AudioOps.ParseFormat(blob);
            Check(f != null && f.Name == "Dolby Atmos for Home Theater" && f.Guid == "{A289735D-FA3E-4E35-9D7D-B6F896ACB2E7}", "parses a format record with the serialization header");
            SpatialFormat f2 = AudioOps.ParseFormat(FormatRecord("Windows Sonic for Headphones", new Guid("b53d940c-b846-4831-9f76-d102b9b725a0"), false));
            Check(f2 != null && f2.Name == "Windows Sonic for Headphones" && f2.Guid == "{B53D940C-B846-4831-9F76-D102B9B725A0}", "parses a format record without the header");
            Check(AudioOps.ParseFormat(new byte[40]) == null && AudioOps.ParseFormat(null) == null, "rejects short or empty records");
            AudioDevice d = new AudioDevice();
            d.Formats.Add(f);
            Check(AudioOps.ResolveFormat(d, "off") == AudioOps.OffGuid && AudioOps.ResolveFormat(d, "") == AudioOps.OffGuid, "off resolves to the zero GUID");
            Check(AudioOps.ResolveFormat(d, "atmos") == f.Guid, "partial name resolves");
            Check(AudioOps.ResolveFormat(d, f.Guid) == f.Guid, "GUID resolves");
            Check(Throws(delegate { AudioOps.ResolveFormat(d, "nonsense format"); }), "unknown format is rejected");

            Dictionary<string, object> r = new Dictionary<string, object>();
            r["passed"] = passed;
            r["failed"] = new List<object>(failed.ToArray());
            r["ok"] = failed.Count == 0;
            return r;
        }

        static bool Throws(Action a)
        {
            try { a(); return false; } catch (Exception) { return true; }
        }

        // Builds a record shaped like the real ones: [8-byte header] name (UTF-16, NUL-terminated) ... GUID at name+768.
        static byte[] FormatRecord(string name, Guid g, bool withHeader)
        {
            int header = withHeader ? 8 : 0;
            byte[] b = new byte[header + 834];
            if (withHeader) { b[0] = 0x41; b[4] = 1; }
            byte[] n = System.Text.Encoding.Unicode.GetBytes(name);
            Array.Copy(n, 0, b, header, n.Length);
            Array.Copy(g.ToByteArray(), 0, b, header + 768, 16);
            return b;
        }
    }
}
