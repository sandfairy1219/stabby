using SharpDX.Direct3D11;
using SharpDX.DXGI;
using System;
using System.Runtime.InteropServices;
using Windows.Graphics.DirectX.Direct3D11;
using WinRT;

namespace Stabby.Services;

public static class Direct3DDeviceHelper
{
    [DllImport("d3d11.dll", EntryPoint = "CreateDirect3D11DeviceFromDXGIDevice", SetLastError = true, CharSet = CharSet.Unicode, ExactSpelling = true, CallingConvention = CallingConvention.StdCall)]
    private static extern uint CreateDirect3D11DeviceFromDXGIDevice(IntPtr dxgiDevice, out IntPtr graphicsDevice);

    public static IDirect3DDevice CreateDevice()
    {
        using var d3dDevice = new SharpDX.Direct3D11.Device(SharpDX.Direct3D.DriverType.Hardware, SharpDX.Direct3D11.DeviceCreationFlags.BgraSupport);
        using var dxgiDevice = d3dDevice.QueryInterface<SharpDX.DXGI.Device>();
        var dxgiDevicePtr = dxgiDevice.NativePointer;

        var hr = CreateDirect3D11DeviceFromDXGIDevice(dxgiDevicePtr, out var devicePtr);
        Marshal.ThrowExceptionForHR((int)hr);

        var device = MarshalInterface<IDirect3DDevice>.FromAbi(devicePtr);
        Marshal.Release(devicePtr);

        return device;
    }
}
