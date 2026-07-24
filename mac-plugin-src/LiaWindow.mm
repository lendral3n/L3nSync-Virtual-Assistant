// LiaWindow.mm — native macOS helper untuk LiaVA desktop mascot.
//
// Overlay desktop-pet: borderless, transparan, floating (selalu di atas), fullscreen,
// click-through toggle + hit-test. Transparansi: cari CAMetalLayer Unity rekursif di
// SELURUH view/layer tree + set opaque=NO BERULANG (Unity bikin layer setelah frame
// pertama & bisa reset opaque tiap frame → harus di-re-assert dari main thread).

#import <Cocoa/Cocoa.h>
#import <QuartzCore/QuartzCore.h>
#import <QuartzCore/CAMetalLayer.h>
#import <CoreGraphics/CoreGraphics.h>

// Jendela render utama Unity = window visible dengan contentView terbesar.
static NSWindow *LiaMainWindow(void) {
    NSWindow *best = nil;
    CGFloat bestArea = 0;
    for (NSWindow *w in [NSApp windows]) {
        if (![w isVisible] || w.contentView == nil) continue;
        CGFloat area = w.frame.size.width * w.frame.size.height;
        if (area > bestArea) { bestArea = area; best = w; }
    }
    return best;
}

static void LiaLayerTreeTransparent(CALayer *layer) {
    if (layer == nil) return;
    layer.opaque = NO;
    if ([layer isKindOfClass:[CAMetalLayer class]]) {
        CAMetalLayer *ml = (CAMetalLayer *)layer;
        ml.opaque = NO;
        ml.backgroundColor = CGColorGetConstantColor(kCGColorClear);
    }
    for (CALayer *sub in layer.sublayers) LiaLayerTreeTransparent(sub);
}

static void LiaViewTreeTransparent(NSView *v) {
    if (v == nil) return;
    if (v.layer != nil) LiaLayerTreeTransparent(v.layer);
    for (NSView *sv in v.subviews) LiaViewTreeTransparent(sv);
}

// Re-assert transparansi (murah; dipanggil tiap frame dari main-thread Unity).
static void LiaApplyTransparency(NSWindow *win) {
    if (win == nil) return;
    [win setOpaque:NO];
    [win setBackgroundColor:[NSColor clearColor]];
    if (win.contentView != nil) LiaViewTreeTransparent(win.contentView);
}

extern "C" {

// Setup jendela jadi overlay borderless fullscreen floating (sekali).
void LiaWindow_MakeOverlay(void) {
    dispatch_async(dispatch_get_main_queue(), ^{
        NSWindow *win = LiaMainWindow();
        if (win == nil) return;

        NSScreen *screen = win.screen ?: [NSScreen mainScreen];
        NSRect full = screen.frame;

        // JANGAN clear NSWindowStyleMaskFullScreen (crash). Unity di-set windowed.
        if ((win.styleMask & NSWindowStyleMaskFullScreen) == 0)
            win.styleMask = NSWindowStyleMaskBorderless;
        [win setHasShadow:NO];
        [win setLevel:NSFloatingWindowLevel];
        win.collectionBehavior = NSWindowCollectionBehaviorCanJoinAllSpaces |
                                 NSWindowCollectionBehaviorFullScreenAuxiliary |
                                 NSWindowCollectionBehaviorStationary;
        [win setMovable:NO];
        [win setFrame:full display:YES];
        LiaApplyTransparency(win);
        [win makeKeyAndOrderFront:nil];
    });
}

// Re-assert transparansi — dipanggil tiap frame dari Unity LateUpdate (main thread).
void LiaWindow_KeepTransparent(void) {
    LiaApplyTransparency(LiaMainWindow());
}

void LiaWindow_SetClickThrough(int enabled) {
    dispatch_async(dispatch_get_main_queue(), ^{
        NSWindow *win = LiaMainWindow();
        if (win != nil) [win setIgnoresMouseEvents:(enabled != 0)];
    });
}

// Cari titik X (points, dari kiri) di tengah CELAH TERLEBAR yang bebas window app,
// di pita bawah layar (y dari bandTopFromTop ke bawah). Untuk roam ke atas wallpaper.
// Return -1 kalau tak ada celah cukup lebar.
float LiaWindow_FreeFloorX(float bandTopFromTop) {
    NSScreen *scr = [NSScreen mainScreen];
    CGFloat W = scr.frame.size.width;
    CGFloat H = scr.frame.size.height;

    CFArrayRef wins = CGWindowListCopyWindowInfo(
        kCGWindowListOptionOnScreenOnly | kCGWindowListExcludeDesktopElements, kCGNullWindowID);
    if (wins == NULL) return (float)(W * 0.5);

    NSMutableArray<NSArray<NSNumber *> *> *iv = [NSMutableArray array];
    CFIndex n = CFArrayGetCount(wins);
    for (CFIndex i = 0; i < n; i++) {
        NSDictionary *w = (__bridge NSDictionary *)CFArrayGetValueAtIndex(wins, i);
        NSString *owner = w[(id)kCGWindowOwnerName];
        if ([owner isEqualToString:@"Lia VA"] || [owner isEqualToString:@"LiaVA"]) continue;
        NSNumber *layer = w[(id)kCGWindowLayer];
        if (layer != nil && layer.intValue != 0) continue; // hanya window normal
        NSDictionary *bd = w[(id)kCGWindowBounds];
        if (bd == nil) continue;
        CGRect r;
        if (!CGRectMakeWithDictionaryRepresentation((__bridge CFDictionaryRef)bd, &r)) continue;
        if (r.size.width < 60 || r.size.height < 60) continue;           // abaikan window kecil
        if (r.origin.y + r.size.height < bandTopFromTop) continue;        // window di atas pita
        [iv addObject:@[@(r.origin.x), @(r.origin.x + r.size.width)]];
    }
    CFRelease(wins);

    [iv sortUsingComparator:^NSComparisonResult(NSArray<NSNumber *> *a, NSArray<NSNumber *> *b) {
        return [a[0] compare:b[0]];
    }];

    CGFloat cursor = 0, bestStart = 0, bestLen = 0;
    for (NSArray<NSNumber *> *p in iv) {
        CGFloat s = MAX(0.0, p[0].doubleValue);
        CGFloat e = MIN(W, p[1].doubleValue);
        if (s > cursor) { CGFloat len = s - cursor; if (len > bestLen) { bestLen = len; bestStart = cursor; } }
        if (e > cursor) cursor = e;
    }
    if (W - cursor > bestLen) { bestLen = W - cursor; bestStart = cursor; }

    if (bestLen < 90) return -1.0f;                        // tak ada ruang cukup
    return (float)(bestStart + bestLen * 0.5);             // tengah celah terlebar
}

float LiaWindow_MouseX(void) { return (float)[NSEvent mouseLocation].x; }
float LiaWindow_MouseY(void) { return (float)[NSEvent mouseLocation].y; }
float LiaWindow_ScreenWidth(void)  { return (float)[NSScreen mainScreen].frame.size.width; }
float LiaWindow_ScreenHeight(void) { return (float)[NSScreen mainScreen].frame.size.height; }

} // extern "C"
