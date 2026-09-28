using System.Runtime.InteropServices;

namespace StudyStash.Core.Ai;

/// <summary>
/// A Mac's own reading of documents, the way Preview and Photos read them: PDFKit for a PDF's typed text, Core
/// Graphics to draw a page that has none, and the Vision framework's text recognition (the accurate kind, which reads
/// handwriting) for that page or a photo. Hand-written Objective-C calls like <c>MacPermissions</c>: nothing to
/// install, and every call is synchronous, so no callbacks. Each call runs in an autorelease pool of its own, so a
/// long document's pages don't pile up in memory on a thread-pool thread.
/// </summary>
internal static class MacVision
{
    const string ObjC = "/usr/lib/libobjc.A.dylib";
    const string CoreGraphics = "/System/Library/Frameworks/CoreGraphics.framework/CoreGraphics";

    [DllImport(ObjC)]
    static extern IntPtr objc_getClass(string name);

    [DllImport(ObjC)]
    static extern IntPtr sel_registerName(string name);

    [DllImport(ObjC)]
    static extern IntPtr objc_autoreleasePoolPush();

    [DllImport(ObjC)]
    static extern void objc_autoreleasePoolPop(IntPtr pool);

    [DllImport(ObjC, EntryPoint = "objc_msgSend")]
    static extern IntPtr Send(IntPtr receiver, IntPtr selector);

    [DllImport(ObjC, EntryPoint = "objc_msgSend")]
    static extern IntPtr Send(IntPtr receiver, IntPtr selector, IntPtr arg);

    [DllImport(ObjC, EntryPoint = "objc_msgSend")]
    static extern IntPtr Send(IntPtr receiver, IntPtr selector, IntPtr arg1, IntPtr arg2);

    [DllImport(ObjC, EntryPoint = "objc_msgSend")]
    static extern IntPtr SendIndex(IntPtr receiver, IntPtr selector, nint index);

    [DllImport(ObjC, EntryPoint = "objc_msgSend")]
    static extern nint SendInt(IntPtr receiver, IntPtr selector);

    [DllImport(ObjC, EntryPoint = "objc_msgSend")]
    static extern byte SendFlag(IntPtr receiver, IntPtr selector);

    [DllImport(ObjC, EntryPoint = "objc_msgSend")]
    static extern void SendSet(IntPtr receiver, IntPtr selector, nint value);

    [DllImport(ObjC, EntryPoint = "objc_msgSend")]
    static extern byte SendBool(IntPtr receiver, IntPtr selector, IntPtr arg);

    [DllImport(ObjC, EntryPoint = "objc_msgSend")]
    static extern byte SendPerform(IntPtr receiver, IntPtr selector, IntPtr requests, out IntPtr error);

    [DllImport(ObjC, EntryPoint = "objc_msgSend")]
    static extern IntPtr SendText(IntPtr receiver, IntPtr selector, [MarshalAs(UnmanagedType.LPUTF8Str)] string text);

    [StructLayout(LayoutKind.Sequential)]
    struct CGRect(double x, double y, double width, double height)
    {
        public double X = x, Y = y, Width = width, Height = height;
    }

    [StructLayout(LayoutKind.Sequential)]
    struct CGAffineTransform
    {
        public double A, B, C, D, Tx, Ty;
    }

    [DllImport(CoreGraphics)]
    static extern IntPtr CGPDFDocumentCreateWithURL(IntPtr url);

    [DllImport(CoreGraphics)]
    static extern nint CGPDFDocumentGetNumberOfPages(IntPtr document);

    [DllImport(CoreGraphics)]
    static extern IntPtr CGPDFDocumentGetPage(IntPtr document, nint pageNumber);

    [DllImport(CoreGraphics)]
    static extern void CGPDFDocumentRelease(IntPtr document);

    [DllImport(CoreGraphics)]
    static extern CGRect CGPDFPageGetBoxRect(IntPtr page, int box);

    [DllImport(CoreGraphics)]
    static extern int CGPDFPageGetRotationAngle(IntPtr page);

    [DllImport(CoreGraphics)]
    static extern CGAffineTransform CGPDFPageGetDrawingTransform(IntPtr page, int box, CGRect rect, int rotate, byte preserveAspectRatio);

    [DllImport(CoreGraphics)]
    static extern IntPtr CGColorSpaceCreateDeviceRGB();

    [DllImport(CoreGraphics)]
    static extern void CGColorSpaceRelease(IntPtr space);

    [DllImport(CoreGraphics)]
    static extern IntPtr CGBitmapContextCreate(IntPtr data, nuint width, nuint height, nuint bitsPerComponent, nuint bytesPerRow, IntPtr space, uint bitmapInfo);

    [DllImport(CoreGraphics)]
    static extern IntPtr CGBitmapContextCreateImage(IntPtr context);

    [DllImport(CoreGraphics)]
    static extern void CGContextRelease(IntPtr context);

    [DllImport(CoreGraphics)]
    static extern void CGContextSetRGBFillColor(IntPtr context, double red, double green, double blue, double alpha);

    [DllImport(CoreGraphics)]
    static extern void CGContextFillRect(IntPtr context, CGRect rect);

    [DllImport(CoreGraphics)]
    static extern void CGContextScaleCTM(IntPtr context, double sx, double sy);

    [DllImport(CoreGraphics)]
    static extern void CGContextConcatCTM(IntPtr context, CGAffineTransform transform);

    [DllImport(CoreGraphics)]
    static extern void CGContextDrawPDFPage(IntPtr context, IntPtr page);

    [DllImport(CoreGraphics)]
    static extern void CGImageRelease(IntPtr image);

    const int CropBox = 1;
    const uint PremultipliedLast = 1;

    /// <summary>A drawn page's longer side at most, in pixels: about 250 dots per inch on a letter page, enough for
    /// small handwriting, without a poster-sized page taking all the memory.</summary>
    const double MaxSide = 2800;

    static readonly Lazy<bool> loaded = new(() =>
    {
        try
        {
            foreach (string f in new[] { "Foundation", "PDFKit", "Vision" })
                NativeLibrary.Load($"/System/Library/Frameworks/{f}.framework/{f}");
            return objc_getClass("VNRecognizeTextRequest") != IntPtr.Zero && objc_getClass("PDFDocument") != IntPtr.Zero;
        }
        catch (Exception e) when (e is DllNotFoundException or EntryPointNotFoundException)
        {
            return false;
        }
    });

    /// <summary>Whether this Mac has PDFKit and Vision's text recognition (every macOS since Catalina).</summary>
    public static bool Available => OperatingSystem.IsMacOS() && loaded.Value;

    static IntPtr Sel(string name) => sel_registerName(name);

    static IntPtr FileUrl(string path) => Send(objc_getClass("NSURL"), Sel("fileURLWithPath:"), SendText(objc_getClass("NSString"), Sel("stringWithUTF8String:"), path));

    static string? Text(IntPtr nsString) =>
        nsString == IntPtr.Zero ? null : Marshal.PtrToStringUTF8(Send(nsString, Sel("UTF8String")));

    static T Pooled<T>(Func<T> work)
    {
        IntPtr pool = objc_autoreleasePoolPush();
        try
        {
            return work();
        }
        finally
        {
            objc_autoreleasePoolPop(pool);
        }
    }

    /// <summary>The typed text on each of a PDF's first <paramref name="max"/> pages, as PDFKit reads it ("" for a
    /// page with none); null when it isn't a PDF that opens (or is locked with a password).</summary>
    public static List<string>? PdfPages(string path, int max, CancellationToken ct)
    {
        if (!Available) return null;
        return Pooled(() =>
        {
            IntPtr doc = Send(Send(objc_getClass("PDFDocument"), Sel("alloc")), Sel("initWithURL:"), FileUrl(path));
            if (doc == IntPtr.Zero) return null;
            try
            {
                if (SendFlag(doc, Sel("isLocked")) != 0) return null;
                var pages = new List<string>();
                nint count = Math.Min(SendInt(doc, Sel("pageCount")), max);
                for (nint i = 0; i < count; i++)
                {
                    ct.ThrowIfCancellationRequested();
                    nint at = i;
                    pages.Add(Pooled(() =>
                    {
                        IntPtr page = SendIndex(doc, Sel("pageAtIndex:"), at);
                        return page == IntPtr.Zero ? "" : Text(Send(page, Sel("string"))) ?? "";
                    }));
                }
                return pages;
            }
            finally
            {
                Send(doc, Sel("release"));
            }
        });
    }

    /// <summary>The words Vision reads in a picture (png, jpg, heic and the rest ImageIO opens), line by line; null
    /// when it can't open it.</summary>
    public static string? ImageText(string path)
    {
        if (!Available) return null;
        return Pooled(() =>
        {
            IntPtr handler = Send(Send(objc_getClass("VNImageRequestHandler"), Sel("alloc")), Sel("initWithURL:options:"),
                FileUrl(path), Send(objc_getClass("NSDictionary"), Sel("dictionary")));
            try
            {
                return Recognize(handler);
            }
            finally
            {
                Send(handler, Sel("release"));
            }
        });
    }

    /// <summary>The words Vision reads on each of these pages of a PDF (numbered from 0), each drawn as a picture
    /// first. Stops at <paramref name="until"/>; a page it couldn't read is left out.</summary>
    public static Dictionary<int, string> PdfPageText(string path, IReadOnlyList<int> pages, DateTime until, CancellationToken ct)
    {
        var read = new Dictionary<int, string>();
        if (!Available) return read;
        Pooled(() =>
        {
            IntPtr doc = CGPDFDocumentCreateWithURL(FileUrl(path));
            if (doc == IntPtr.Zero) return 0;
            try
            {
                nint count = CGPDFDocumentGetNumberOfPages(doc);
                foreach (int i in pages)
                {
                    ct.ThrowIfCancellationRequested();
                    if (DateTime.UtcNow >= until) break;
                    if (i < 0 || i >= count) continue;
                    if (Pooled(() => PageText(CGPDFDocumentGetPage(doc, i + 1))) is { } text) read[i] = text;
                }
            }
            finally
            {
                CGPDFDocumentRelease(doc);
            }
            return 0;
        });
        return read;
    }

    /// <summary>Draws one page on white, big enough for handwriting, and reads it.</summary>
    static string? PageText(IntPtr page)
    {
        if (page == IntPtr.Zero) return null;
        var box = CGPDFPageGetBoxRect(page, CropBox);
        bool turned = ((CGPDFPageGetRotationAngle(page) % 180) + 180) % 180 != 0;
        double w = turned ? box.Height : box.Width, h = turned ? box.Width : box.Height;
        if (w < 1 || h < 1) return null;
        double scale = Math.Min(3.0, MaxSide / Math.Max(w, h));
        nuint pw = (nuint)Math.Ceiling(w * scale), ph = (nuint)Math.Ceiling(h * scale);
        IntPtr space = CGColorSpaceCreateDeviceRGB();
        IntPtr ctx = CGBitmapContextCreate(IntPtr.Zero, pw, ph, 8, 0, space, PremultipliedLast);
        CGColorSpaceRelease(space);
        if (ctx == IntPtr.Zero) return null;
        IntPtr image;
        try
        {
            CGContextSetRGBFillColor(ctx, 1, 1, 1, 1);
            CGContextFillRect(ctx, new CGRect(0, 0, pw, ph));
            CGContextScaleCTM(ctx, scale, scale);
            // The page's own rotation and offset, at its size: the scale above makes it big.
            CGContextConcatCTM(ctx, CGPDFPageGetDrawingTransform(page, CropBox, new CGRect(0, 0, w, h), 0, 1));
            CGContextDrawPDFPage(ctx, page);
            image = CGBitmapContextCreateImage(ctx);
        }
        finally
        {
            CGContextRelease(ctx);
        }
        if (image == IntPtr.Zero) return null;
        try
        {
            IntPtr handler = Send(Send(objc_getClass("VNImageRequestHandler"), Sel("alloc")), Sel("initWithCGImage:options:"),
                image, Send(objc_getClass("NSDictionary"), Sel("dictionary")));
            try
            {
                return Recognize(handler);
            }
            finally
            {
                Send(handler, Sel("release"));
            }
        }
        finally
        {
            CGImageRelease(image);
        }
    }

    /// <summary>Vision's accurate text recognition, with language correction and the language found on its own: each
    /// line it sees, top to bottom. Null when it fails.</summary>
    static string? Recognize(IntPtr handler)
    {
        if (handler == IntPtr.Zero) return null;
        IntPtr request = Send(Send(objc_getClass("VNRecognizeTextRequest"), Sel("alloc")), Sel("init"));
        try
        {
            SendSet(request, Sel("setRecognitionLevel:"), 0); // VNRequestTextRecognitionLevelAccurate
            SendSet(request, Sel("setUsesLanguageCorrection:"), 1);
            if (SendBool(request, Sel("respondsToSelector:"), Sel("setAutomaticallyDetectsLanguage:")) != 0)
                SendSet(request, Sel("setAutomaticallyDetectsLanguage:"), 1); // macOS 13 and later
            IntPtr requests = Send(objc_getClass("NSArray"), Sel("arrayWithObject:"), request);
            if (SendPerform(handler, Sel("performRequests:error:"), requests, out _) == 0) return null;
            IntPtr results = Send(request, Sel("results"));
            if (results == IntPtr.Zero) return "";
            var lines = new List<string>();
            nint n = SendInt(results, Sel("count"));
            for (nint i = 0; i < n; i++)
            {
                IntPtr candidates = SendIndex(SendIndex(results, Sel("objectAtIndex:"), i), Sel("topCandidates:"), 1);
                if (candidates != IntPtr.Zero && SendInt(candidates, Sel("count")) > 0 && Text(Send(Send(candidates, Sel("firstObject")), Sel("string"))) is { } line)
                    lines.Add(line);
            }
            return string.Join("\n", lines);
        }
        finally
        {
            Send(request, Sel("release"));
        }
    }
}
