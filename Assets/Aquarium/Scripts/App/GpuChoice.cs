// Which graphics card draws the aquarium. On Windows the cards are listed through DXGI, and the
// choice is the system's own per-app setting (Settings > Display > Graphics), written for this
// program: Windows starts it on the chosen card next time. Windows offers "let Windows decide",
// "power saving" and "high performance"; the choice is always there, each named with the card
// Windows gives for it. Elsewhere the card is only shown.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using UnityEngine;

namespace Aquarium.App
{
    public static class GpuChoice
    {
        public sealed class Card
        {
            public string name;
            /// <summary>The value of Windows' GpuPreference that leads to this card: 1 power saving, 2 high performance.</summary>
            public int preference;
            public uint vendorId, deviceId;
        }

        /// <summary>The name of the card drawing now.</summary>
        public static string Current => SystemInfo.graphicsDeviceName;

        /// <summary>Whether the card can be chosen here (a Windows build; not in the editor).</summary>
        public static bool CanChoose =>
#if (UNITY_STANDALONE_WIN || UNITY_WEBGL) && !UNITY_EDITOR
            true;
#else
            false;
#endif

        /// <summary>The cards Windows can start the aquarium on (empty if unknown).</summary>
        public static List<Card> Cards()
        {
            var cards = new List<Card>();
#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
            try
            {
                foreach (int pref in new[] { 1, 2 })
                {
                    var d = Dxgi.FirstHardware(pref);
                    if (d == null) continue;
                    var same = cards.Find(c => c.vendorId == d.Value.VendorId && c.deviceId == d.Value.DeviceId);
                    if (same == null) cards.Add(new Card { name = d.Value.Description.Trim(), preference = pref, vendorId = d.Value.VendorId, deviceId = d.Value.DeviceId });
                }
            }
            catch (Exception e) { Debug.LogWarning($"graphics cards: {e.Message}"); }
#endif
            return cards;
        }

#if UNITY_WEBGL && !UNITY_EDITOR
        [DllImport("__Internal")] static extern string AqGpuGet();
        [DllImport("__Internal")] static extern void AqGpuSet(string v);
        [DllImport("__Internal")] static extern void AqReload();
#endif

        /// <summary>The card Windows gives for a preference (1 power saving, 2 high performance), or null.</summary>
        public static string NameFor(int preference)
        {
#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
            try { return Dxgi.FirstHardware(preference)?.Description.Trim(); }
            catch (Exception e) { Debug.LogWarning($"graphics cards: {e.Message}"); }
#endif
            return null;
        }

        /// <summary>The card drawing now, among the listed ones.</summary>
        public static Card CurrentOf(List<Card> cards) =>
            cards.Find(c => c.vendorId == SystemInfo.graphicsDeviceVendorID && c.deviceId == SystemInfo.graphicsDeviceID)
            ?? cards.Find(c => c.name == SystemInfo.graphicsDeviceName);

        /// <summary>The choice saved for this program (0: Windows decides).</summary>
        public static int Saved()
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            return AqGpuGet() switch { "default" => 0, "low-power" => 1, _ => 2 };
#endif
#if UNITY_STANDALONE_WIN && !UNITY_EDITOR
            var v = Win.Read(Win.Exe);
            if (v != null)
                foreach (var part in v.Split(';'))
                    if (part.StartsWith("GpuPreference=") && int.TryParse(part.Substring(14), out int p)) return p;
#endif
            return 0;
        }

        /// <summary>Save the choice for this program; it takes effect on the next start.</summary>
        public static void Choose(int preference)
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            // in a browser: the wish for the WebGL context, read by the page at the next load
            AqGpuSet(preference == 0 ? "default" : preference == 1 ? "low-power" : "high-performance");
#endif
#if UNITY_STANDALONE_WIN && !UNITY_EDITOR
            // keep the other settings Windows may keep in the same value
            var parts = (Win.Read(Win.Exe) ?? "").Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries).Where(p => !p.StartsWith("GpuPreference=")).ToList();
            parts.Add($"GpuPreference={preference};");
            Win.Write(Win.Exe, string.Join(";", parts.Select(p => p.TrimEnd(';'))) + ";");
#endif
        }

        /// <summary>Start again (on the chosen card).</summary>
        public static void Restart()
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            AqReload();
#endif
#if UNITY_STANDALONE_WIN && !UNITY_EDITOR
            var args = Environment.GetCommandLineArgs().Skip(1).Select(a => a.Contains(' ') ? $"\"{a}\"" : a);
            System.Diagnostics.Process.Start(Win.Exe, string.Join(" ", args));
            Application.Quit();
#endif
        }

#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
        /// <summary>Just enough DXGI to ask which card Windows gives for a preference.</summary>
        static class Dxgi
        {
            [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
            public struct AdapterDesc1
            {
                [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string Description;
                public uint VendorId, DeviceId, SubSysId, Revision;
                public UIntPtr DedicatedVideoMemory, DedicatedSystemMemory, SharedSystemMemory;
                public uint LuidLow;
                public int LuidHigh;
                public uint Flags;
            }

            [DllImport("dxgi.dll")] static extern int CreateDXGIFactory1(ref Guid riid, out IntPtr factory);

            [UnmanagedFunctionPointer(CallingConvention.StdCall)] delegate int QueryInterfaceFn(IntPtr self, ref Guid iid, out IntPtr obj);
            [UnmanagedFunctionPointer(CallingConvention.StdCall)] delegate uint ReleaseFn(IntPtr self);
            [UnmanagedFunctionPointer(CallingConvention.StdCall)] delegate int EnumByPreferenceFn(IntPtr self, uint index, int preference, ref Guid iid, out IntPtr adapter);
            [UnmanagedFunctionPointer(CallingConvention.StdCall)] delegate int GetDesc1Fn(IntPtr self, out AdapterDesc1 desc);

            static Guid Factory1 = new Guid("770aae78-f26f-4dba-a829-253c83d1b387");
            static Guid Factory6 = new Guid("c1b6694f-ff09-44a9-b03c-77900a0a1d17");
            static Guid Adapter1 = new Guid("29038f61-3839-4626-91fd-086879011a05");
            const uint SoftwareFlag = 2;

            static T Method<T>(IntPtr obj, int slot) where T : Delegate =>
                Marshal.GetDelegateForFunctionPointer<T>(Marshal.ReadIntPtr(Marshal.ReadIntPtr(obj), slot * IntPtr.Size));

            static void Release(IntPtr obj) { if (obj != IntPtr.Zero) Method<ReleaseFn>(obj, 2)(obj); }

            /// <summary>The first real (not software) card Windows lists for a preference: 1 power saving, 2 high performance.</summary>
            public static AdapterDesc1? FirstHardware(int preference)
            {
                if (CreateDXGIFactory1(ref Factory1, out var f1) < 0) return null;
                IntPtr f6 = IntPtr.Zero;
                try
                {
                    // IUnknown::QueryInterface is slot 0; IDXGIFactory6::EnumAdapterByGpuPreference is slot 29
                    if (Method<QueryInterfaceFn>(f1, 0)(f1, ref Factory6, out f6) < 0) return null;
                    var enumerate = Method<EnumByPreferenceFn>(f6, 29);
                    for (uint i = 0; i < 16; i++)
                    {
                        if (enumerate(f6, i, preference, ref Adapter1, out var a) < 0) break;
                        try
                        {
                            // IDXGIAdapter1::GetDesc1 is slot 10
                            if (Method<GetDesc1Fn>(a, 10)(a, out var d) >= 0 && (d.Flags & SoftwareFlag) == 0) return d;
                        }
                        finally { Release(a); }
                    }
                    return null;
                }
                finally
                {
                    Release(f6);
                    Release(f1);
                }
            }
        }

        /// <summary>Windows' per-program graphics preference (HKCU\Software\Microsoft\DirectX\UserGpuPreferences).</summary>
        static class Win
        {
            static readonly UIntPtr CurrentUser = (UIntPtr)0x80000001u;
            const string Key = @"Software\Microsoft\DirectX\UserGpuPreferences";

            [DllImport("advapi32.dll", CharSet = CharSet.Unicode)] static extern int RegGetValueW(UIntPtr key, string sub, string value, uint flags, out uint type, StringBuilder data, ref uint size);
            [DllImport("advapi32.dll", CharSet = CharSet.Unicode)] static extern int RegSetKeyValueW(UIntPtr key, string sub, string value, uint type, string data, uint size);
            [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] static extern uint GetModuleFileNameW(IntPtr module, StringBuilder name, uint size);

            public static string Exe
            {
                get
                {
                    var sb = new StringBuilder(1024);
                    GetModuleFileNameW(IntPtr.Zero, sb, (uint)sb.Capacity);
                    return sb.ToString();
                }
            }

            public static string Read(string name)
            {
                uint size = 2048;
                var sb = new StringBuilder((int)size / 2);
                return RegGetValueW(CurrentUser, Key, name, 0x2 /* RRF_RT_REG_SZ */, out _, sb, ref size) == 0 ? sb.ToString() : null;
            }

            public static void Write(string name, string value) =>
                RegSetKeyValueW(CurrentUser, Key, name, 1 /* REG_SZ */, value, (uint)((value.Length + 1) * 2));
        }
#endif
    }
}
