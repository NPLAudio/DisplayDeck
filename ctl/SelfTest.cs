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

            // ---- layout builder: an edited layout becomes a profile ----
            List<Profile> templates = new List<Profile>(); templates.Add(p);
            List<LiveTarget> noLive = new List<LiveTarget>();
            List<SpecDisplay> spec = new List<SpecDisplay>();
            SpecDisplay s0 = new SpecDisplay(); s0.TargetId = 4353; s0.Name = "MPG"; s0.Width = 5120; s0.Height = 1440; s0.X = 0; s0.Y = 0; s0.Primary = true;
            SpecDisplay s1 = new SpecDisplay(); s1.TargetId = 4355; s1.Name = "TV"; s1.Width = 3840; s1.Height = 2160; s1.X = -3840; s1.Y = 100; s1.Refresh = 120; s1.Primary = false;
            spec.Add(s0); spec.Add(s1);
            Profile b1 = LayoutBuilder.Build(spec, templates, noLive);
            Check(b1.Paths.Count == 2 && b1.Monitors.Count == 2, "builds a path and a monitor per enabled display");
            Check(b1.Modes[(int)b1.Paths[0].sourceInfo.modeInfoIdx].sourceMode.positionX == 0 && b1.Modes[(int)b1.Paths[1].sourceInfo.modeInfoIdx].sourceMode.positionX == -3840, "keeps positions, including negative ones");
            Check(b1.Paths[0].targetInfo.modeInfoIdx != DisplayNative.IDX_INVALID && b1.Paths[1].targetInfo.modeInfoIdx == DisplayNative.IDX_INVALID, "keeps timing only for an unchanged display");
            Check(b1.Paths[1].targetInfo.refreshRate.Numerator == 120000 && b1.Paths[1].targetInfo.refreshRate.Denominator == 1000, "sets the new refresh rate");
            Check(b1.Paths[0].sourceInfo.id == 0 && b1.Paths[1].sourceInfo.id == 1, "gives each display its own source");

            // moving the primary: everything shifts so the primary sits at (0,0)
            s0.Primary = false; s1.Primary = true;
            Profile b2 = LayoutBuilder.Build(spec, templates, noLive);
            ModeInfo tvSrc = b2.Modes[(int)b2.Paths[1].sourceInfo.modeInfoIdx], mpgSrc = b2.Modes[(int)b2.Paths[0].sourceInfo.modeInfoIdx];
            Check(tvSrc.sourceMode.positionX == 0 && tvSrc.sourceMode.positionY == 0 && mpgSrc.sourceMode.positionX == 3840 && mpgSrc.sourceMode.positionY == -100, "shifts the layout so the primary is at the origin");

            // a display that is switched off is left out; a layout with none on is refused
            s1.Enabled = false; s0.Primary = true;
            Check(LayoutBuilder.Build(spec, templates, noLive).Paths.Count == 1, "leaves out displays that are off");
            s0.Enabled = false;
            Check(Throws(delegate { LayoutBuilder.Build(spec, templates, noLive); }), "refuses a layout with every display off");
            s0.Enabled = true; s1.Enabled = true;

            // a display nobody knows about cannot be added; a live one can (no saved timing)
            SpecDisplay s2 = new SpecDisplay(); s2.TargetId = 9999; s2.Name = "New"; s2.Width = 1920; s2.Height = 1080; s2.X = 5120; s2.Refresh = 60;
            spec.Add(s2);
            Check(Throws(delegate { LayoutBuilder.Build(spec, templates, noLive); }), "refuses a display that is neither connected nor saved");
            List<LiveTarget> live = new List<LiveTarget>();
            LiveTarget lt = new LiveTarget(); lt.Path = p.Paths[1]; lt.Path.targetInfo.id = 9999; lt.Monitor = new MonitorInfo(); lt.Monitor.FriendlyName = "New"; live.Add(lt);
            s0.Primary = true; s1.Primary = false;
            Profile b3 = LayoutBuilder.Build(spec, templates, live);
            Check(b3.Paths.Count == 3 && b3.Paths[2].targetInfo.id == 9999 && b3.Paths[2].targetInfo.modeInfoIdx == DisplayNative.IDX_INVALID, "adds a connected display from the live list");
            Check(b3.Monitors[2].TargetId() == 9999, "synthesizes a device path for it");

            // validation
            s2.Width = 10;
            Check(Throws(delegate { LayoutBuilder.Build(spec, templates, live); }), "refuses an impossible resolution");
            s2.Width = 1920; s2.TargetId = 4353;
            Check(Throws(delegate { LayoutBuilder.Build(spec, templates, live); }), "refuses the same display twice");

            // the built profile survives writing and reading, and can be applied by the normal path
            Profile rt = ProfileFile.Parse(ProfileFile.Write(b1));
            Check(rt.Paths.Count == 2 && rt.Paths[1].targetInfo.modeInfoIdx == DisplayNative.IDX_INVALID, "an edited profile round-trips through XML");
            PathInfo[] cur2 = new PathInfo[2]; cur2[0] = rt.Paths[0]; cur2[1] = rt.Paths[1];
            cur2[0].targetInfo.adapterId.LowPart = 3333; cur2[1].targetInfo.adapterId.LowPart = 3333;
            List<string> sk2 = new List<string>();
            List<MatchedDisplay> m2 = DisplayOps.Match(rt, cur2, sk2);
            PathInfo[] bp; ModeInfo[] bm;
            DisplayOps.Build(rt, m2, out bp, out bm);
            Check(bp.Length == 2 && bm.Length == 3 && bp[1].targetInfo.modeInfoIdx == DisplayNative.IDX_INVALID, "apply copes with a display that has no saved timing");

            // the layout JSON the editor sends
            JavaScriptSerializerShim shim = new JavaScriptSerializerShim();
            List<SpecDisplay> parsed = LayoutBuilder.ParseSpec(shim.Parse("{\"displays\":[{\"targetId\":4353,\"name\":\"A\",\"enabled\":true,\"primary\":true,\"width\":2560,\"height\":1440,\"x\":0,\"y\":0,\"refresh\":143.97,\"rotation\":1}]}"));
            Check(parsed.Count == 1 && parsed[0].TargetId == 4353 && parsed[0].Width == 2560 && Math.Abs(parsed[0].Refresh - 143.97) < 0.001 && parsed[0].Primary, "reads the layout json");

            // ---- DDC/CI capabilities string ----
            Capabilities cap = Ddc.ParseCapabilities("(prot(monitor)type(LCD)model(MPG 491C)cmds(01 02 03 07 0C E3 F3)vcp(02 04 05 08 10 12 14(01 05 08 0B) 16 18 1A 52 60(0F 11 12 1B) 62 87 8D AC AE B2 D6(01 04 05) DC(00 03 05) E0 E1 E2(00 01 02 0A))mswhql(1)asset_eep(40)mccs_ver(2.2))");
            Check(cap.Model == "MPG 491C" && cap.Type == "LCD" && cap.MccsVersion == "2.2", "reads the model, type and MCCS version");
            Check(cap.Codes.ContainsKey(0x10) && cap.Codes.ContainsKey(0x62) && cap.Codes.ContainsKey(0xD6), "lists the supported feature codes");
            Check(cap.Codes[0x60].Count == 4 && cap.Codes[0x60][0] == 0x0F && cap.Codes[0x60][3] == 0x1B, "lists the inputs the monitor offers");
            Check(cap.Codes[0x10].Count == 0, "a plain feature has no value list");
            Check(cap.Codes[0xD6].Count == 3 && cap.Codes[0x14].Count == 4, "lists power and color preset values");
            Check(cap.Codes.ContainsKey(0xE2) && cap.Codes[0xE2].Count == 4, "reads the last feature in the list");
            Capabilities nested = Ddc.ParseCapabilities("(vcp(10 60(11(01 02) 12) 62)model(X(1)))");
            Check(nested.Codes[0x60].Count == 2 && nested.Codes.ContainsKey(0x62), "skips nested groups instead of losing track");
            Check(Ddc.ParseCapabilities("").Codes.Count == 0 && Ddc.ParseCapabilities(null).Codes.Count == 0, "an empty capabilities string lists nothing");
            Check(Ddc.IsComplete("(prot(monitor)vcp(10 60(0F 11)))") && !Ddc.IsComplete("(prot(monitor)vcp(10 60(0F 1") && !Ddc.IsComplete("") && !Ddc.IsComplete(null) && !Ddc.IsComplete("vcp(10)"), "tells a complete capabilities string from a cut-off one");
            Check(Ddc.ConnectorName(10) == "displayport" && Ddc.ConnectorName(5) == "hdmi" && Ddc.ConnectorName(4) == "dvi" && Ddc.ConnectorName(0) == "vga" && Ddc.ConnectorName(99) == "other", "names the cable a display uses");
            Check(Ddc.IsBlocked(0x04) && Ddc.IsBlocked(0x08) && !Ddc.IsBlocked(0x60) && !Ddc.IsBlocked(0x10), "factory reset codes are blocked");

            // extras: scaling steps, wallpaper colors and which live display a saved entry belongs to
            int rec, cur, max;
            Extras.StepsFromRelative(-2, 1, 4, out rec, out cur, out max);
            Check(rec == 2 && cur == 3 && max == 6 && Extras.DpiSteps[rec] == 150 && Extras.DpiSteps[cur] == 175, "scaling steps are read relative to the recommended one");
            Check(Extras.StepIndex(125) == 1 && Extras.StepIndex(130) == -1, "scaling percentages map to steps");
            Check(Extras.FormatColor(Extras.ParseColor("#1e2f3a")) == "#1e2f3a" && Extras.ParseColor("#ff0000") == 0x0000FF, "wallpaper colors convert both ways");
            Check(Throws(delegate { Extras.ParseColor("red"); }), "rejects a color that is not #rrggbb");
            List<LiveInfo> liveSet = new List<LiveInfo>();
            liveSet.Add(FakeLive(4353, 26934, 16296, true));
            liveSet.Add(FakeLive(4356, 1, 1, true));
            liveSet.Add(FakeLive(4400, 26934, 16296, false));
            Check(Extras.Match(liveSet, 4356, "") == liveSet[1], "a saved display is found by its id");
            Check(Extras.Match(liveSet, 9999, Extras.HwIdOf(liveSet[1])) == liveSet[1], "otherwise by its make and model when only one matches");
            Check(Extras.Match(liveSet, 9999, "NOPE000") == null && Extras.Match(liveSet, 4400, "") == null, "a display that is off or unknown is not matched");
            liveSet.Add(FakeLive(4500, 1, 1, true));
            Check(Extras.Match(liveSet, 9999, Extras.HwIdOf(liveSet[1])) == null, "two displays of the same model are never guessed between");

            Dictionary<string, object> r = new Dictionary<string, object>();
            r["passed"] = passed;
            r["failed"] = new List<object>(failed.ToArray());
            r["ok"] = failed.Count == 0;
            return r;
        }

        class JavaScriptSerializerShim
        {
            public object Parse(string json) { return new System.Web.Script.Serialization.JavaScriptSerializer().DeserializeObject(json); }
        }

        static LiveInfo FakeLive(uint targetId, ushort maker, ushort product, bool active)
        {
            LiveInfo li = new LiveInfo();
            li.Target = new LiveTarget();
            li.Target.Path.targetInfo.id = targetId;
            li.Target.Monitor = new MonitorInfo();
            li.Target.Monitor.ManufactureId = maker;
            li.Target.Monitor.ProductCodeId = product;
            li.Active = active;
            return li;
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
