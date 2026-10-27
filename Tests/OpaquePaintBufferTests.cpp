#include "../Fences.Native/OpaquePaintBuffer.h"
#include <iostream>
#include <stdexcept>

int main()
{
    HDC display = GetDC(nullptr);
    try
    {
        const auto resources = GetGuiResources(GetCurrentProcess(), GR_GDIOBJECTS);
        {
            OpaquePaintBuffer surface;
            if (!surface.Ensure(display, 32, 16)) throw std::runtime_error("allocate opaque paint surface");
            const auto bitmap = GetCurrentObject(surface.DC(), OBJ_BITMAP);
            DIBSECTION data = {};
            if (GetObjectW(bitmap, sizeof(data), &data) != sizeof(data) || !data.dsBm.bmBits) throw std::runtime_error("paint surface must expose 32-bit pixels");
            auto colors = static_cast<DWORD*>(data.dsBm.bmBits);
            for (int index = 0; index < 512; ++index) colors[index] = 0x00112233u;
            surface.MakeOpaque({ 3, 4, 9, 11 });
            for (int y = 0; y < 16; ++y)
                for (int x = 0; x < 32; ++x)
                {
                    const DWORD expected = x >= 3 && x < 9 && y >= 4 && y < 11 ? 0xff112233u : 0x00112233u;
                    if (colors[y * 32 + x] != expected) throw std::runtime_error("alpha repair must preserve RGB and untouched regions");
                }
            std::cout << "PASS: dirty-region alpha is opaque with unchanged RGB; surrounding pixels are untouched" << std::endl;
            if (!surface.Ensure(display, 32, 16) || GetCurrentObject(surface.DC(), OBJ_BITMAP) != bitmap)
                throw std::runtime_error("ordinary paints must reuse their bitmap");
            if (!surface.Ensure(display, 64, 24)) throw std::runtime_error("resize paint surface");
            surface.MakeOpaque({ -100, -100, 1000, 1000 });
            std::cout << "PASS: bitmap reuse, resize, and out-of-bounds clipping" << std::endl;
        }
        if (GetGuiResources(GetCurrentProcess(), GR_GDIOBJECTS) != resources) throw std::runtime_error("paint surface leaked GDI resources");
        std::cout << "PASS: all owned bitmap/DC resources released" << std::endl;
        ReleaseDC(nullptr, display); return 0;
    }
    catch (const std::exception& error) { std::cerr << "FAIL: " << error.what() << std::endl; }
    ReleaseDC(nullptr, display); return 1;
}
