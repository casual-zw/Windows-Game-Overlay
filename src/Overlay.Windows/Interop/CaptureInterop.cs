using System.Runtime.InteropServices;
using Windows.Graphics.Capture;
using Windows.Graphics.DirectX.Direct3D11;

namespace Overlay.Windows.Interop;

internal static class CaptureInterop
{
    [DllImport("d3d11.dll", ExactSpelling = true)]
    private static extern int D3D11CreateDevice(nint adapter, uint driverType, nint software, uint flags,
        nint featureLevels, uint featureLevelCount, uint sdkVersion,
        out nint device, out uint featureLevel, out nint immediateContext);

    [DllImport("d3d11.dll", ExactSpelling = true)]
    private static extern int CreateDirect3D11DeviceFromDXGIDevice(nint dxgiDevice, out nint device);

    [DllImport("combase.dll", ExactSpelling = true, CharSet = CharSet.Unicode)]
    private static extern int WindowsCreateString(string source, int length, out nint value);
    [DllImport("combase.dll", ExactSpelling = true)]
    private static extern int WindowsDeleteString(nint value);
    [DllImport("combase.dll", ExactSpelling = true)]
    private static extern int RoGetActivationFactory(nint classId, in Guid iid, out nint factory);

    internal static IDirect3DDevice CreateDevice()
    {
        nint device = 0, context = 0, dxgi = 0, inspectable = 0;
        try
        {
            // BGRA support is required for WGC's B8G8R8A8 surfaces.
            Marshal.ThrowExceptionForHR(D3D11CreateDevice(0, 1 /* hardware */, 0, 0x20, 0, 0, 7,
                out device, out _, out context));
            var iid = new Guid("54ec77fa-1377-44e6-8c32-88fd5f44c84c"); // IDXGIDevice
            Marshal.ThrowExceptionForHR(Marshal.QueryInterface(device, in iid, out dxgi));
            Marshal.ThrowExceptionForHR(CreateDirect3D11DeviceFromDXGIDevice(dxgi, out inspectable));
            return WinRT.MarshalInterface<IDirect3DDevice>.FromAbi(inspectable);
        }
        finally
        {
            foreach (var pointer in new[] { inspectable, dxgi, context, device })
                if (pointer != 0) Marshal.Release(pointer);
        }
    }

    internal static unsafe GraphicsCaptureItem CreateItem(nint hwnd)
    {
        const string runtimeClass = "Windows.Graphics.Capture.GraphicsCaptureItem";
        nint hstring = 0, factory = 0, item = 0;
        try
        {
            Marshal.ThrowExceptionForHR(WindowsCreateString(runtimeClass, runtimeClass.Length, out hstring));
            var interopId = new Guid("3628e81b-3cac-4c60-b7f4-23ce0e0c3356");
            Marshal.ThrowExceptionForHR(RoGetActivationFactory(hstring, in interopId, out factory));
            var itemId = new Guid("79c3f95b-31f7-4ec2-a464-632ef5d30760");
            // IGraphicsCaptureItemInterop inherits IUnknown; CreateForWindow is slot 3.
            var create = (delegate* unmanaged[Stdcall]<nint, nint, Guid*, nint*, int>)(*(nint**)factory)[3];
            Marshal.ThrowExceptionForHR(create(factory, hwnd, &itemId, &item));
            return WinRT.MarshalInspectable<GraphicsCaptureItem>.FromAbi(item);
        }
        finally
        {
            if (item != 0) Marshal.Release(item);
            if (factory != 0) Marshal.Release(factory);
            if (hstring != 0) WindowsDeleteString(hstring);
        }
    }
}
