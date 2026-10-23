using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Win32.SafeHandles;

namespace MchoseHub.Hardware
{
    public class HidDeviceInfo
    {
        public string DevicePath { get; set; } = string.Empty;
        public ushort VendorId { get; set; }
        public ushort ProductId { get; set; }
        public ushort VersionNumber { get; set; }
        public string Manufacturer { get; set; } = string.Empty;
        public string Product { get; set; } = string.Empty;
        public ushort UsagePage { get; set; }
        public ushort Usage { get; set; }
        public short InputReportByteLength { get; set; }
        public short OutputReportByteLength { get; set; }
        public short FeatureReportByteLength { get; set; }

        public override string ToString() =>
            $"{Product} (VID: 0x{VendorId:X4}, PID: 0x{ProductId:X4}, UsagePage: 0x{UsagePage:X4}, FeatureLen: {FeatureReportByteLength})";
    }

    public class NativeHid : IDisposable
    {
        #region Win32 P/Invoke Declarations

        private const uint GENERIC_READ = 0x80000000;
        private const uint GENERIC_WRITE = 0x40000000;
        private const uint FILE_SHARE_READ = 0x00000001;
        private const uint FILE_SHARE_WRITE = 0x00000002;
        private const uint OPEN_EXISTING = 3;
        private const uint FILE_FLAG_OVERLAPPED = 0x40000000;
        private const uint FILE_ATTRIBUTE_NORMAL = 0x00000080;

        private const int DIGCF_PRESENT = 0x00000002;
        private const int DIGCF_DEVICEINTERFACE = 0x00000010;

        [StructLayout(LayoutKind.Sequential)]
        private struct SP_DEVICE_INTERFACE_DATA
        {
            public int cbSize;
            public Guid InterfaceClassGuid;
            public int Flags;
            public IntPtr Reserved;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
        private struct SP_DEVICE_INTERFACE_DETAIL_DATA
        {
            public int cbSize;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
            public string DevicePath;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct HIDD_ATTRIBUTES
        {
            public int Size;
            public ushort VendorID;
            public ushort ProductID;
            public ushort VersionNumber;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct HIDP_CAPS
        {
            public ushort Usage;
            public ushort UsagePage;
            public ushort InputReportByteLength;
            public ushort OutputReportByteLength;
            public ushort FeatureReportByteLength;
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 17)]
            public ushort[] Reserved;
            public ushort NumberLinkCollectionNodes;
            public ushort NumberInputButtonCaps;
            public ushort NumberInputValueCaps;
            public ushort NumberInputDataIndices;
            public ushort NumberOutputButtonCaps;
            public ushort NumberOutputValueCaps;
            public ushort NumberOutputDataIndices;
            public ushort NumberFeatureButtonCaps;
            public ushort NumberFeatureValueCaps;
            public ushort NumberFeatureDataIndices;
        }

        [DllImport("hid.dll", SetLastError = true)]
        private static extern void HidD_GetHidGuid(out Guid HidGuid);

        [DllImport("setupapi.dll", SetLastError = true, CharSet = CharSet.Auto)]
        private static extern IntPtr SetupDiGetClassDevs(ref Guid ClassGuid, IntPtr Enumerator, IntPtr hwndParent, int Flags);

        [DllImport("setupapi.dll", SetLastError = true)]
        private static extern bool SetupDiEnumDeviceInterfaces(IntPtr DeviceInfoSet, IntPtr DeviceInfoData, ref Guid InterfaceClassGuid, int MemberIndex, ref SP_DEVICE_INTERFACE_DATA DeviceInterfaceData);

        [DllImport("setupapi.dll", SetLastError = true, CharSet = CharSet.Auto)]
        private static extern bool SetupDiGetDeviceInterfaceDetail(IntPtr DeviceInfoSet, ref SP_DEVICE_INTERFACE_DATA DeviceInterfaceData, IntPtr DeviceInterfaceDetailData, int DeviceInterfaceDetailDataSize, ref int RequiredSize, IntPtr DeviceInfoData);

        [DllImport("setupapi.dll", SetLastError = true)]
        private static extern bool SetupDiDestroyDeviceInfoList(IntPtr DeviceInfoSet);

        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Auto)]
        private static extern SafeFileHandle CreateFile(
            string lpFileName,
            uint dwDesiredAccess,
            uint dwShareMode,
            IntPtr lpSecurityAttributes,
            uint dwCreationDisposition,
            uint dwFlagsAndAttributes,
            IntPtr hTemplateFile);

        [DllImport("hid.dll", SetLastError = true)]
        private static extern bool HidD_GetAttributes(SafeFileHandle HidDeviceObject, ref HIDD_ATTRIBUTES Attributes);

        [DllImport("hid.dll", SetLastError = true)]
        private static extern bool HidD_GetPreparsedData(SafeFileHandle HidDeviceObject, out IntPtr PreparsedData);

        [DllImport("hid.dll", SetLastError = true)]
        private static extern bool HidD_FreePreparsedData(IntPtr PreparsedData);

        [DllImport("hid.dll", SetLastError = true)]
        private static extern int HidP_GetCaps(IntPtr PreparsedData, ref HIDP_CAPS Capabilities);

        [DllImport("hid.dll", SetLastError = true)]
        private static extern bool HidD_SetFeature(SafeFileHandle HidDeviceObject, byte[] lpReportBuffer, int ReportBufferLength);

        [DllImport("hid.dll", SetLastError = true)]
        private static extern bool HidD_GetFeature(SafeFileHandle HidDeviceObject, byte[] lpReportBuffer, int ReportBufferLength);

        [DllImport("hid.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern bool HidD_GetProductString(SafeFileHandle HidDeviceObject, byte[] Buffer, int BufferLength);

        [DllImport("hid.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern bool HidD_GetManufacturerString(SafeFileHandle HidDeviceObject, byte[] Buffer, int BufferLength);

        #endregion

        public static readonly ushort[] SupportedVids = { 0x3837, 0x5253 };

        /// <summary>Vendor HID usage page that exposes the mouse configuration protocol.</summary>
        public const ushort ConfigUsagePage = 0xFF01;

        private SafeFileHandle? _deviceHandle;
        private FileStream? _readStream;
        private CancellationTokenSource? _readCts;
        private HidDeviceInfo? _currentDeviceInfo;

        // Guards the native handle against UI-thread Disconnect() racing the protocol worker.
        private readonly object _deviceLock = new();
        private bool _disposed;
        private bool _deviceLostReported;

        private const int ERROR_INVALID_HANDLE = 6;
        private const int ERROR_DEVICE_NOT_CONNECTED = 1167;

        public bool IsConnected
        {
            get
            {
                lock (_deviceLock)
                {
                    return _deviceHandle != null && !_deviceHandle.IsInvalid && !_deviceHandle.IsClosed;
                }
            }
        }
        public HidDeviceInfo? CurrentDevice => _currentDeviceInfo;

        public event Action<byte[]>? InputReportReceived;
        public event Action<string, string>? LogMessage;

        /// <summary>
        /// Raised once when an I/O transfer or the input stream proves the device is gone
        /// (cable pulled, dongle removed, mouse powered off).
        /// </summary>
        public event Action? DeviceRemoved;

        /// <summary>
        /// Cheap topology probe used for hot-plug handling: is any supported MCHOSE HID interface present?
        /// </summary>
        public static bool IsSupportedDevicePresent() => EnumerateDevices().Count > 0;

        /// <summary>
        /// Verifies that the currently opened interface is still enumerated by Windows.
        /// </summary>
        public bool IsCurrentDevicePresent()
        {
            var info = _currentDeviceInfo;
            if (info == null) return false;

            return EnumerateDevices()
                .Any(d => string.Equals(d.DevicePath, info.DevicePath, StringComparison.OrdinalIgnoreCase));
        }

        private void HandleTransferFailure(int error)
        {
            if (error != ERROR_DEVICE_NOT_CONNECTED && error != ERROR_INVALID_HANDLE)
            {
                return;
            }

            HandleDeviceLost($"Win32 error {error}");
        }

        private void HandleDeviceLost(string reason)
        {
            lock (_deviceLock)
            {
                if (_deviceLostReported || _deviceHandle == null) return;
                _deviceLostReported = true;
            }

            Log($"Device lost ({reason}). Waiting for it to come back...", "Warn");
            Disconnect();
            DeviceRemoved?.Invoke();
        }

        private void Log(string msg, string type = "Info")
        {
            App.DebugLog($"[HID:{type}] {msg}");
            LogMessage?.Invoke(msg, type);
        }

        public static List<HidDeviceInfo> EnumerateDevices()
        {
            var list = new List<HidDeviceInfo>();
            HidD_GetHidGuid(out Guid hidGuid);

            IntPtr devInfoSet = SetupDiGetClassDevs(ref hidGuid, IntPtr.Zero, IntPtr.Zero, DIGCF_PRESENT | DIGCF_DEVICEINTERFACE);
            if (devInfoSet == IntPtr.Zero || devInfoSet.ToInt64() == -1)
                return list;

            try
            {
                var ifData = new SP_DEVICE_INTERFACE_DATA();
                ifData.cbSize = Marshal.SizeOf(ifData);

                for (int i = 0; SetupDiEnumDeviceInterfaces(devInfoSet, IntPtr.Zero, ref hidGuid, i, ref ifData); i++)
                {
                    int requiredSize = 0;
                    SetupDiGetDeviceInterfaceDetail(devInfoSet, ref ifData, IntPtr.Zero, 0, ref requiredSize, IntPtr.Zero);
                    if (requiredSize == 0) continue;

                    IntPtr detailDataBuffer = Marshal.AllocHGlobal(requiredSize);
                    try
                    {
                        // cbSize on x64 is 8 bytes, on x86 is 5 bytes
                        Marshal.WriteInt32(detailDataBuffer, IntPtr.Size == 8 ? 8 : 5);

                        if (SetupDiGetDeviceInterfaceDetail(devInfoSet, ref ifData, detailDataBuffer, requiredSize, ref requiredSize, IntPtr.Zero))
                        {
                            IntPtr pDevicePath = new IntPtr(detailDataBuffer.ToInt64() + 4);
                            string? devicePath = Marshal.PtrToStringAuto(pDevicePath);
                            if (string.IsNullOrEmpty(devicePath)) continue;

                            using var handle = CreateFile(devicePath, 0, FILE_SHARE_READ | FILE_SHARE_WRITE, IntPtr.Zero, OPEN_EXISTING, 0, IntPtr.Zero);
                            if (handle.IsInvalid) continue;

                            var attrs = new HIDD_ATTRIBUTES();
                            attrs.Size = Marshal.SizeOf(attrs);
                            if (HidD_GetAttributes(handle, ref attrs))
                            {
                                bool isVidMatch = false;
                                foreach (var vid in SupportedVids)
                                {
                                    if (attrs.VendorID == vid)
                                    {
                                        isVidMatch = true;
                                        break;
                                    }
                                }

                                if (isVidMatch)
                                {
                                    var info = new HidDeviceInfo
                                    {
                                        DevicePath = devicePath,
                                        VendorId = attrs.VendorID,
                                        ProductId = attrs.ProductID,
                                        VersionNumber = attrs.VersionNumber
                                    };

                                    // Capabilities
                                    if (HidD_GetPreparsedData(handle, out IntPtr pData))
                                    {
                                        try
                                        {
                                            var caps = new HIDP_CAPS();
                                            int status = HidP_GetCaps(pData, ref caps);
                                            if (status == 0x00110000 || caps.UsagePage > 0)
                                            {
                                                info.UsagePage = caps.UsagePage;
                                                info.Usage = caps.Usage;
                                                info.InputReportByteLength = (short)caps.InputReportByteLength;
                                                info.OutputReportByteLength = (short)caps.OutputReportByteLength;
                                                info.FeatureReportByteLength = (short)caps.FeatureReportByteLength;
                                            }
                                        }
                                        finally
                                        {
                                            HidD_FreePreparsedData(pData);
                                        }
                                    }

                                    // Product String
                                    byte[] strBuf = new byte[256];
                                    if (HidD_GetProductString(handle, strBuf, strBuf.Length))
                                    {
                                        info.Product = Encoding.Unicode.GetString(strBuf).TrimEnd('\0');
                                    }
                                    if (string.IsNullOrEmpty(info.Product))
                                    {
                                        info.Product = "MCHOSE Gaming Mouse";
                                    }

                                    list.Add(info);
                                }
                            }
                        }
                    }
                    finally
                    {
                        Marshal.FreeHGlobal(detailDataBuffer);
                    }
                }
            }
            finally
            {
                SetupDiDestroyDeviceInfoList(devInfoSet);
            }

            return list;
        }

        public bool Connect(HidDeviceInfo? target = null)
        {
            if (_disposed) return false;

            Disconnect();

            var devices = EnumerateDevices();
            if (devices.Count == 0)
            {
                Log("No MCHOSE HID devices detected.", "Warn");
                return false;
            }

            // Diagnostic dump: with both the cable and the 2.4G dongle plugged in there are
            // several interfaces sharing VID/PID families, and picking the wrong one looks
            // exactly like "the mouse is not detected".
            App.DebugLog($"[HID] {devices.Count} supported interface(s) found:");
            foreach (var candidate in devices)
            {
                App.DebugLog($"  - {candidate} | path={candidate.DevicePath}");
            }

            // CRITICAL: the official MCHOSE vendor config interface is ALWAYS UsagePage 0xFF01
            // (65281). UsagePage 0xFF0B is auxiliary/macro and rejects feature reports 0x11/0x12.
            //
            // With the cable and the 2.4G dongle both plugged in there are TWO 0xFF01 links
            // (e.g. PID 0x100B wired + PID 0x4019 dongle). Opening a link proves nothing: the
            // inactive one still accepts feature requests but answers with empty payloads.
            // The caller validates the link with a real settings read and tries the next one.
            HidDeviceInfo? chosen = target
                ?? devices.Find(d => d.UsagePage == ConfigUsagePage)
                ?? devices.Find(d => d.FeatureReportByteLength >= 64 && d.UsagePage != 0xFF0B)
                ?? devices.Find(d => d.FeatureReportByteLength >= 20 && d.UsagePage != 0xFF0B)
                ?? devices[0];

            SafeFileHandle? handle = OpenDeviceHandle(chosen);
            if (handle.IsInvalid)
            {
                int err = Marshal.GetLastWin32Error();
                Log($"Failed to open device handle (Win32 error: {err})", "Error");
                handle.Dispose();
                return false;
            }

            Log($"Opening device interface: {chosen.Product} (VID: 0x{chosen.VendorId:X4}, PID: 0x{chosen.ProductId:X4}, UsagePage: 0x{chosen.UsagePage:X4}, FeatureReportByteLength: {chosen.FeatureReportByteLength})", "Info");

            lock (_deviceLock)
            {
                if (_disposed)
                {
                    handle.Dispose();
                    return false;
                }

                _deviceHandle = handle;
                _currentDeviceInfo = chosen;
            }
            StartInputReader(chosen);
            Log($"Successfully connected to {chosen.Product}!", "Info");
            return true;
        }

        /// <summary>
        /// Configuration links currently enumerated (wired mouse + 2.4G dongle). Only one of
        /// them carries live data at any time.
        /// </summary>
        public static List<HidDeviceInfo> ConfigCandidates() =>
            EnumerateDevices().Where(d => d.UsagePage == ConfigUsagePage).ToList();

        /// <summary>
        /// Number of candidate configuration links currently enumerated (wired mouse + dongle).
        /// </summary>
        public static int ConfigInterfaceCount() => ConfigCandidates().Count;

        private SafeFileHandle OpenDeviceHandle(HidDeviceInfo info) =>
            CreateFile(
                info.DevicePath,
                GENERIC_READ | GENERIC_WRITE,
                FILE_SHARE_READ | FILE_SHARE_WRITE,
                IntPtr.Zero,
                OPEN_EXISTING,
                FILE_FLAG_OVERLAPPED | FILE_ATTRIBUTE_NORMAL,
                IntPtr.Zero);

        private void StartInputReader(HidDeviceInfo dev)
        {
            try
            {
                int inLen = dev.InputReportByteLength > 0 ? dev.InputReportByteLength : 64;
                _readStream = new FileStream(_deviceHandle!, FileAccess.Read, inLen, isAsync: true);
                var stream = _readStream;
                _readCts = new CancellationTokenSource();
                var token = _readCts.Token;

                Task.Run(async () =>
                {
                    byte[] buffer = new byte[inLen];
                    while (!token.IsCancellationRequested && IsConnected)
                    {
                        try
                        {
                            int bytesRead = await stream.ReadAsync(buffer.AsMemory(0, inLen), token);
                            if (bytesRead > 0)
                            {
                                byte[] report = new byte[bytesRead];
                                Array.Copy(buffer, report, bytesRead);
                                InputReportReceived?.Invoke(report);
                            }
                        }
                        catch (OperationCanceledException)
                        {
                            break;
                        }
                        catch (ObjectDisposedException)
                        {
                            break;
                        }
                        catch (Exception ex)
                        {
                            if (!token.IsCancellationRequested)
                            {
                                Log($"Input report stream error: {ex.Message}", "Warn");
                                // A dead pipe means the mouse/dongle went away; let the UI react.
                                HandleDeviceLost("input stream closed");
                            }
                            break;
                        }
                    }
                }, token);
            }
            catch (Exception ex)
            {
                Log($"Could not start background input reader: {ex.Message}", "Warn");
            }
        }

        public bool SendFeatureReport(byte reportId, byte[] payload)
        {
            // Windows HidD_SetFeature requires 1 byte Report ID + payload bytes
            // In MCHOSE HID descriptor:
            // Report 0x11 has 20 data bytes -> buffer length is 21 bytes
            // Report 0x12 has 64 data bytes -> buffer length is 65 bytes
            int targetLength = (reportId == 0x11) ? 21 : 65;
            if (payload.Length + 1 > targetLength) targetLength = payload.Length + 1;

            byte[] buffer = new byte[targetLength];
            Array.Fill<byte>(buffer, 0xFF);
            buffer[0] = reportId;

            int copyLen = Math.Min(payload.Length, targetLength - 1);
            Array.Copy(payload, 0, buffer, 1, copyLen);

            bool result;
            int error = 0;

            lock (_deviceLock)
            {
                if (_deviceHandle == null || _deviceHandle.IsInvalid || _deviceHandle.IsClosed)
                {
                    Log("Cannot send feature report: device disconnected.", "Error");
                    return false;
                }

                string hexDump = BitConverter.ToString(buffer).Replace("-", " ");
                Log($"TX Feature (Report 0x{reportId:X2}, len {buffer.Length}): {hexDump}", "Tx");

                result = HidD_SetFeature(_deviceHandle, buffer, buffer.Length);
                if (!result)
                {
                    error = Marshal.GetLastWin32Error();
                }
            }

            if (!result)
            {
                Log($"HidD_SetFeature (Report 0x{reportId:X2}) failed with Win32 error: {error}", "Error");
                HandleTransferFailure(error);
            }

            return result;
        }

        public byte[]? GetFeatureReport(byte reportId, int expectedLength)
        {
            int targetLength = (reportId == 0x11) ? 21 : 65;
            if (expectedLength + 1 > targetLength) targetLength = expectedLength + 1;

            byte[] buffer = new byte[targetLength];
            Array.Fill<byte>(buffer, 0xFF);
            buffer[0] = reportId;

            bool success;
            int error = 0;

            lock (_deviceLock)
            {
                if (_deviceHandle == null || _deviceHandle.IsInvalid || _deviceHandle.IsClosed)
                {
                    Log("Cannot get feature report: device disconnected.", "Error");
                    return null;
                }

                success = HidD_GetFeature(_deviceHandle, buffer, buffer.Length);
                if (!success)
                {
                    error = Marshal.GetLastWin32Error();
                }
            }

            if (!success)
            {
                Log($"HidD_GetFeature (Report 0x{reportId:X2}) failed with Win32 error: {error}", "Error");
                HandleTransferFailure(error);
                return null;
            }

            string hexDump = BitConverter.ToString(buffer).Replace("-", " ");
            Log($"RX Feature (Report 0x{reportId:X2}): {hexDump}", "Rx");

            // Extract payload (skip reportId byte 0)
            byte[] payload = new byte[buffer.Length - 1];
            Array.Copy(buffer, 1, payload, 0, payload.Length);
            return payload;
        }

        public void Disconnect()
        {
            CancellationTokenSource? readCts;
            FileStream? readStream;
            SafeFileHandle? handle;

            lock (_deviceLock)
            {
                readCts = _readCts;
                readStream = _readStream;
                handle = _deviceHandle;

                _readCts = null;
                _readStream = null;
                _deviceHandle = null;
                _currentDeviceInfo = null;
                _deviceLostReported = false;
            }

            try { readCts?.Cancel(); } catch { }
            try { readCts?.Dispose(); } catch { }
            try { readStream?.Dispose(); } catch { }
            try { handle?.Dispose(); } catch { }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            Disconnect();
        }
    }
}
