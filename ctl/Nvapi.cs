// DDC/CI over an NVIDIA graphics card's own I2C channel (NVAPI). Windows' monitor API refuses some NVIDIA-driven
// displays, and this talks to them directly. It is only used when the Windows route does not work.
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;

namespace DisplayDeck.Ctl
{
    public class NvMonitor
    {
        public IntPtr Gpu;
        public uint DisplayId;
    }

    public static class Nvapi
    {
        [DllImport("kernel32.dll", CharSet = CharSet.Ansi)] static extern IntPtr LoadLibrary(string file);
        [DllImport("kernel32.dll", CharSet = CharSet.Ansi)] static extern IntPtr GetProcAddress(IntPtr module, string name);

        delegate IntPtr QueryInterfaceDelegate(uint id);
        delegate int InitializeDelegate();
        delegate int DisplayIdByNameDelegate([MarshalAs(UnmanagedType.LPStr)] string name, out uint displayId);
        delegate int EnumGpusDelegate([Out] IntPtr[] handles, out uint count);
        delegate int ConnectedDisplayIdsDelegate(IntPtr gpu, IntPtr ids, ref uint count, uint flags);
        delegate int I2cDelegate(IntPtr gpu, ref I2cInfo info, ref uint unknown);

        [StructLayout(LayoutKind.Sequential)]
        struct I2cInfo
        {
            public uint version;
            public uint displayMask;
            public byte isDdcPort;
            public byte deviceAddress;
            public IntPtr registerAddress;
            public uint registerAddressSize;
            public IntPtr data;
            public uint dataSize;
            public uint speed;
            public uint speedKhz;
            public byte portId;
            public uint isPortIdSet;
        }

        // Function ids for nvapi_QueryInterface.
        const uint IdInitialize = 0x0150E828, IdDisplayIdByName = 0xAE457190, IdEnumGpus = 0xE5AC921F, IdConnectedDisplayIds = 0x78DBA2,
            IdI2cRead = 0x4D7B0709, IdI2cWrite = 0x283AC65A;

        static readonly object Gate = new object();
        static bool tried, available;
        static QueryInterfaceDelegate query;
        static DisplayIdByNameDelegate displayIdByName;
        static EnumGpusDelegate enumGpus;
        static ConnectedDisplayIdsDelegate connectedIds;
        static I2cDelegate i2cRead, i2cWrite;

        static T Interface<T>(uint id) where T : class
        {
            IntPtr p = query(id);
            return p == IntPtr.Zero ? null : (T)(object)Marshal.GetDelegateForFunctionPointer(p, typeof(T));
        }

        static bool Load()
        {
            if (tried) return available;
            tried = true;
            try
            {
                IntPtr lib = LoadLibrary("nvapi64.dll");
                if (lib == IntPtr.Zero) return false;
                IntPtr qi = GetProcAddress(lib, "nvapi_QueryInterface");
                if (qi == IntPtr.Zero) return false;
                query = (QueryInterfaceDelegate)Marshal.GetDelegateForFunctionPointer(qi, typeof(QueryInterfaceDelegate));
                InitializeDelegate init = Interface<InitializeDelegate>(IdInitialize);
                if (init == null || init() != 0) return false;
                displayIdByName = Interface<DisplayIdByNameDelegate>(IdDisplayIdByName);
                enumGpus = Interface<EnumGpusDelegate>(IdEnumGpus);
                connectedIds = Interface<ConnectedDisplayIdsDelegate>(IdConnectedDisplayIds);
                i2cRead = Interface<I2cDelegate>(IdI2cRead);
                i2cWrite = Interface<I2cDelegate>(IdI2cWrite);
                available = displayIdByName != null && enumGpus != null && connectedIds != null && i2cRead != null && i2cWrite != null;
            }
            catch (Exception) { available = false; }
            return available;
        }

        /// <summary>The NVIDIA display behind a Windows display name (such as \\.\DISPLAY1), or null.</summary>
        public static NvMonitor Open(string gdiName)
        {
            lock (Gate)
            {
                if (!Load()) return null;
                try
                {
                    uint id;
                    if (displayIdByName(gdiName, out id) != 0) return null;
                    IntPtr gpu = GpuOf(id);
                    if (gpu == IntPtr.Zero) return null;
                    NvMonitor m = new NvMonitor();
                    m.Gpu = gpu; m.DisplayId = id;
                    return m;
                }
                catch (Exception) { return null; }
            }
        }

        /// <summary>The graphics card a display id belongs to (every id list entry is 16 bytes: version, connector, id, flags).</summary>
        static IntPtr GpuOf(uint displayId)
        {
            IntPtr[] gpus = new IntPtr[64];
            uint n;
            if (enumGpus(gpus, out n) != 0) return IntPtr.Zero;
            for (int g = 0; g < n; g++)
            {
                uint count = 0;
                if (connectedIds(gpus[g], IntPtr.Zero, ref count, 0) != 0 || count == 0) continue;
                IntPtr arr = Marshal.AllocHGlobal((int)(count * 16));
                try
                {
                    for (int i = 0; i < count * 16; i++) Marshal.WriteByte(arr, i, 0);
                    for (int i = 0; i < count; i++) Marshal.WriteInt32(arr, i * 16, 0x10010);
                    if (connectedIds(gpus[g], arr, ref count, 0) != 0) continue;
                    for (int i = 0; i < count; i++)
                        if ((uint)Marshal.ReadInt32(arr, i * 16 + 8) == displayId) return gpus[g];
                }
                finally { Marshal.FreeHGlobal(arr); }
            }
            return IntPtr.Zero;
        }

        // ---------------- DDC/CI packets over I2C ----------------
        // A DDC/CI message to the monitor (I2C address 0x37, 0x6E as a write address) is
        //   0x51, 0x80 | length, opcode, arguments..., checksum
        // where the checksum is the XOR of 0x6E and every byte before it. The reply starts with 0x6E.

        const byte WriteAddress = 0x6E, ReadAddress = 0x6F, HostSource = 0x51;
        const int Settle = 50; // milliseconds a monitor needs between a request and its reply

        static bool Write(NvMonitor m, byte[] body)
        {
            byte[] data = new byte[body.Length + 1];
            Array.Copy(body, data, body.Length);
            byte sum = (byte)(WriteAddress ^ HostSource);
            foreach (byte b in body) sum ^= b;
            data[body.Length] = sum;

            IntPtr reg = Marshal.AllocHGlobal(1), buf = Marshal.AllocHGlobal(data.Length);
            try
            {
                Marshal.WriteByte(reg, HostSource);
                Marshal.Copy(data, 0, buf, data.Length);
                I2cInfo info = NewInfo(m, WriteAddress);
                info.registerAddress = reg; info.registerAddressSize = 1;
                info.data = buf; info.dataSize = (uint)data.Length;
                uint unknown = 0;
                return i2cWrite(m.Gpu, ref info, ref unknown) == 0;
            }
            finally { Marshal.FreeHGlobal(reg); Marshal.FreeHGlobal(buf); }
        }

        static byte[] Read(NvMonitor m, int count)
        {
            IntPtr buf = Marshal.AllocHGlobal(count);
            try
            {
                for (int i = 0; i < count; i++) Marshal.WriteByte(buf, i, 0);
                I2cInfo info = NewInfo(m, ReadAddress);
                info.data = buf; info.dataSize = (uint)count;
                uint unknown = 0;
                if (i2cRead(m.Gpu, ref info, ref unknown) != 0) return null;
                byte[] r = new byte[count];
                Marshal.Copy(buf, r, 0, count);
                return r;
            }
            finally { Marshal.FreeHGlobal(buf); }
        }

        static I2cInfo NewInfo(NvMonitor m, byte address)
        {
            I2cInfo info = new I2cInfo();
            info.version = (uint)Marshal.SizeOf(typeof(I2cInfo)) | (3u << 16);
            info.displayMask = m.DisplayId;
            info.isDdcPort = 1;
            info.deviceAddress = address;
            info.speed = 0xFFFF;   // the default speed
            return info;
        }

        /// <summary>Reads one feature. Returns null when the monitor says the feature is unsupported or does not answer.</summary>
        public static VcpValue GetVcp(NvMonitor m, int code)
        {
            lock (Gate)
            {
                for (int attempt = 0; attempt < 3; attempt++)
                {
                    if (!Write(m, new byte[] { 0x82, 0x01, (byte)code })) return null;   // not a DDC display at all
                    Thread.Sleep(Settle);
                    byte[] r = Read(m, 11);
                    if (r == null) { Thread.Sleep(40); continue; }
                    int o = r[0] == WriteAddress ? 0 : -1;       // some replies leave out the leading source byte
                    if (o < 0 || r[2 + o] != 0x02) { Thread.Sleep(40); continue; }
                    if (r[3 + o] == 1) return null;               // result 1: unsupported feature
                    if (r[3 + o] != 0 || r[4 + o] != (byte)code) { Thread.Sleep(40); continue; }
                    VcpValue v = new VcpValue();
                    v.Max = (uint)((r[6 + o] << 8) | r[7 + o]);
                    v.Current = (uint)((r[8 + o] << 8) | r[9 + o]);
                    return v;
                }
                return null;
            }
        }

        public static bool SetVcp(NvMonitor m, int code, uint value)
        {
            lock (Gate)
            {
                for (int attempt = 0; attempt < 2; attempt++)
                {
                    if (Write(m, new byte[] { 0x84, 0x03, (byte)code, (byte)(value >> 8), (byte)(value & 0xFF) }))
                    {
                        Thread.Sleep(Settle);
                        return true;
                    }
                    Thread.Sleep(80);
                }
                return false;
            }
        }

        /// <summary>The capabilities string, fetched 32 bytes at a time. Null when the monitor does not answer.</summary>
        public static string ReadCapabilities(NvMonitor m)
        {
            lock (Gate)
            {
                StringBuilder sb = new StringBuilder();
                int offset = 0;
                for (int fragment = 0; fragment < 64 && sb.Length < 4096; fragment++)
                {
                    byte[] data = null;
                    for (int attempt = 0; attempt < 3 && data == null; attempt++)
                    {
                        if (!Write(m, new byte[] { 0x83, 0xF3, (byte)(offset >> 8), (byte)(offset & 0xFF) })) return sb.Length > 0 ? sb.ToString() : null;
                        Thread.Sleep(Settle);
                        byte[] r = Read(m, 38);
                        if (r == null || r[0] != WriteAddress || r[2] != 0xE3) { Thread.Sleep(50); continue; }
                        int len = (r[1] & 0x7F) - 3;               // opcode and the two offset bytes are part of the length
                        if (len < 0 || len > 32) { Thread.Sleep(50); continue; }
                        data = new byte[len];
                        Array.Copy(r, 5, data, 0, len);
                    }
                    if (data == null) return null;
                    if (data.Length == 0) break;
                    sb.Append(Encoding.ASCII.GetString(data));
                    offset += data.Length;
                }
                return sb.Length > 0 ? sb.ToString().Trim().TrimEnd('\0') : null;
            }
        }
    }
}
