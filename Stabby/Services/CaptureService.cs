using Stabby.Models;
using System;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.WindowsRuntime;
using Windows.Graphics;
using Windows.Graphics.Capture;
using Windows.Graphics.DirectX;
using Windows.Graphics.DirectX.Direct3D11;
using Windows.Graphics.Imaging;

namespace Stabby.Services;

public class CaptureService : IDisposable
{
    private GraphicsCaptureItem? _item;
    private Direct3D11CaptureFramePool? _framePool;
    private GraphicsCaptureSession? _session;
    private IDirect3DDevice? _device;
    private SizeInt32 _lastSize;

    public event EventHandler<CapturedFrame>? FrameReady;

    public void StartCapture(GraphicsCaptureItem item)
    {
        try
        {
            StopCapture();
            _item = item;
            _lastSize = item.Size;
            _device = Direct3DDeviceHelper.CreateDevice();
            if (_device == null) throw new InvalidOperationException("Failed to create Direct3D device.");

            _framePool = Direct3D11CaptureFramePool.Create(
                _device,
                DirectXPixelFormat.B8G8R8A8UIntNormalized,
                2,
                item.Size);
            _framePool.FrameArrived += OnFrameArrived;
            _session = _framePool.CreateCaptureSession(item);
            _session.StartCapture();
        }
        catch (Exception ex)
        {
            StopCapture();
            throw new InvalidOperationException($"Capture initialization failed: {ex.Message}", ex);
        }
    }

    public void StopCapture()
    {
        _session?.Dispose();
        _session = null;
        if (_framePool != null)
        {
            _framePool.FrameArrived -= OnFrameArrived;
            _framePool.Dispose();
            _framePool = null;
        }
        _device?.Dispose();
        _device = null;
    }

    private async void OnFrameArrived(Direct3D11CaptureFramePool sender, object args)
    {
        using var frame = sender.TryGetNextFrame();
        if (frame == null) return;

        var size = frame.ContentSize;
        if (size.Width != _lastSize.Width || size.Height != _lastSize.Height)
        {
            _lastSize = size;
            if (_framePool != null && _device != null)
            {
                _framePool.Recreate(_device, DirectXPixelFormat.B8G8R8A8UIntNormalized, 2, size);
            }
        }

        var softwareBitmap = await SoftwareBitmap.CreateCopyFromSurfaceAsync(frame.Surface).AsTask();
        using var converted = SoftwareBitmap.Convert(softwareBitmap, BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied);
        softwareBitmap.Dispose();

        var bytes = new byte[size.Width * size.Height * 4];
        converted.CopyToBuffer(bytes.AsBuffer());

        FrameReady?.Invoke(this, new CapturedFrame(bytes, size.Width, size.Height));
    }

    public void Dispose()
    {
        StopCapture();
        GC.SuppressFinalize(this);
    }
}

public class CapturedFrame : EventArgs
{
    public byte[] Data { get; }
    public int Width { get; }
    public int Height { get; }

    public CapturedFrame(byte[] data, int width, int height)
    {
        Data = data;
        Width = width;
        Height = height;
    }
}
