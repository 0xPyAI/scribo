using System;
using System.IO;
using System.Windows;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Microsoft.Win32;

namespace Scribo;

public static class ScreenCaptureHelper
{
    public static BitmapSource? CaptureScreen(Window? excludeWindow = null)
    {
        bool wasVisible = false;
        try
        {
            if (excludeWindow != null && excludeWindow.IsVisible)
            {
                wasVisible = true;
                excludeWindow.Visibility = Visibility.Collapsed;
                DoEvents();
                System.Threading.Thread.Sleep(160);
            }

            int screenLeft = (int)SystemParameters.VirtualScreenLeft;
            int screenTop = (int)SystemParameters.VirtualScreenTop;
            int screenWidth = (int)SystemParameters.VirtualScreenWidth;
            int screenHeight = (int)SystemParameters.VirtualScreenHeight;

            if (screenWidth <= 0 || screenHeight <= 0) return null;

            IntPtr hdcScreen = IntPtr.Zero;
            IntPtr hdcMem = IntPtr.Zero;
            IntPtr hBitmap = IntPtr.Zero;
            IntPtr hOld = IntPtr.Zero;

            try
            {
                hdcScreen = NativeMethods.GetDC(IntPtr.Zero);
                if (hdcScreen == IntPtr.Zero) return null;

                hdcMem = NativeMethods.CreateCompatibleDC(hdcScreen);
                hBitmap = NativeMethods.CreateCompatibleBitmap(hdcScreen, screenWidth, screenHeight);
                hOld = NativeMethods.SelectObject(hdcMem, hBitmap);

                NativeMethods.BitBlt(hdcMem, 0, 0, screenWidth, screenHeight, hdcScreen, screenLeft, screenTop, NativeMethods.SRCCOPY | NativeMethods.CAPTUREBLT);

                NativeMethods.SelectObject(hdcMem, hOld);
                hOld = IntPtr.Zero;

                BitmapSource source = System.Windows.Interop.Imaging.CreateBitmapSourceFromHBitmap(
                    hBitmap,
                    IntPtr.Zero,
                    Int32Rect.Empty,
                    BitmapSizeOptions.FromEmptyOptions());

                source.Freeze();
                return source;
            }
            finally
            {
                if (hOld != IntPtr.Zero && hdcMem != IntPtr.Zero)
                {
                    NativeMethods.SelectObject(hdcMem, hOld);
                }
                if (hBitmap != IntPtr.Zero)
                {
                    NativeMethods.DeleteObject(hBitmap);
                }
                if (hdcMem != IntPtr.Zero)
                {
                    NativeMethods.DeleteDC(hdcMem);
                }
                if (hdcScreen != IntPtr.Zero)
                {
                    NativeMethods.ReleaseDC(IntPtr.Zero, hdcScreen);
                }
            }
        }
        catch (Exception ex)
        {
            Logger.LogException("ScreenCaptureHelper.CaptureScreen", ex);
            return null;
        }
        finally
        {
            if (wasVisible && excludeWindow != null)
            {
                excludeWindow.Visibility = Visibility.Visible;
            }
        }
    }

    public static BitmapSource? CaptureScreenRegion(Rect screenRect, Window? excludeWindow = null)
    {
        bool wasVisible = false;
        try
        {
            if (excludeWindow != null && excludeWindow.IsVisible)
            {
                wasVisible = true;
                excludeWindow.Visibility = Visibility.Collapsed;
                DoEvents();
                System.Threading.Thread.Sleep(100);
            }

            int x = (int)screenRect.Left;
            int y = (int)screenRect.Top;
            int width = (int)Math.Max(1, screenRect.Width);
            int height = (int)Math.Max(1, screenRect.Height);

            IntPtr hdcScreen = IntPtr.Zero;
            IntPtr hdcMem = IntPtr.Zero;
            IntPtr hBitmap = IntPtr.Zero;
            IntPtr hOld = IntPtr.Zero;

            try
            {
                hdcScreen = NativeMethods.GetDC(IntPtr.Zero);
                if (hdcScreen == IntPtr.Zero) return null;

                hdcMem = NativeMethods.CreateCompatibleDC(hdcScreen);
                hBitmap = NativeMethods.CreateCompatibleBitmap(hdcScreen, width, height);
                hOld = NativeMethods.SelectObject(hdcMem, hBitmap);

                NativeMethods.BitBlt(hdcMem, 0, 0, width, height, hdcScreen, x, y, NativeMethods.SRCCOPY | NativeMethods.CAPTUREBLT);

                NativeMethods.SelectObject(hdcMem, hOld);
                hOld = IntPtr.Zero;

                BitmapSource source = System.Windows.Interop.Imaging.CreateBitmapSourceFromHBitmap(
                    hBitmap,
                    IntPtr.Zero,
                    Int32Rect.Empty,
                    BitmapSizeOptions.FromEmptyOptions());

                source.Freeze();
                return source;
            }
            finally
            {
                if (hOld != IntPtr.Zero && hdcMem != IntPtr.Zero)
                {
                    NativeMethods.SelectObject(hdcMem, hOld);
                }
                if (hBitmap != IntPtr.Zero)
                {
                    NativeMethods.DeleteObject(hBitmap);
                }
                if (hdcMem != IntPtr.Zero)
                {
                    NativeMethods.DeleteDC(hdcMem);
                }
                if (hdcScreen != IntPtr.Zero)
                {
                    NativeMethods.ReleaseDC(IntPtr.Zero, hdcScreen);
                }
            }
        }
        catch (Exception ex)
        {
            Logger.LogException("ScreenCaptureHelper.CaptureScreenRegion", ex);
            return null;
        }
        finally
        {
            if (wasVisible && excludeWindow != null)
            {
                excludeWindow.Visibility = Visibility.Visible;
            }
        }
    }

    public static void CopyToClipboard(BitmapSource bitmap)
    {
        for (int i = 0; i < 5; i++)
        {
            try
            {
                Clipboard.SetImage(bitmap);
                return;
            }
            catch (System.Runtime.InteropServices.ExternalException)
            {
                System.Threading.Thread.Sleep(50);
            }
            catch (Exception ex)
            {
                Logger.LogException("ScreenCaptureHelper.CopyToClipboard", ex);
            }
        }
    }

    public static bool SaveToFile(BitmapSource bitmap, Window owner)
    {
        try
        {
            var sfd = new SaveFileDialog
            {
                Title = "Save Screenshot to PC",
                Filter = "PNG Image (*.png)|*.png|JPEG Image (*.jpg)|*.jpg",
                DefaultExt = ".png",
                FileName = $"Scribo_{DateTime.Now:yyyyMMdd_HHmmss}.png",
                InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.MyPictures)
            };

            if (sfd.ShowDialog(owner) == true)
            {
                using var stream = new FileStream(sfd.FileName, FileMode.Create);
                BitmapEncoder encoder;
                if (sfd.FileName.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase) || sfd.FileName.EndsWith(".jpeg", StringComparison.OrdinalIgnoreCase))
                {
                    encoder = new JpegBitmapEncoder { QualityLevel = 95 };
                }
                else
                {
                    encoder = new PngBitmapEncoder();
                }

                encoder.Frames.Add(BitmapFrame.Create(bitmap));
                encoder.Save(stream);
                return true;
            }
        }
        catch (Exception ex)
        {
            Logger.LogException("ScreenCaptureHelper.SaveToFile", ex);
        }

        return false;
    }

    private static void DoEvents()
    {
        try
        {
            var frame = new DispatcherFrame();
            Dispatcher.CurrentDispatcher.BeginInvoke(
                DispatcherPriority.Render,
                new DispatcherOperationCallback(f =>
                {
                    ((DispatcherFrame)f).Continue = false;
                    return null;
                }), frame);
            Dispatcher.PushFrame(frame);
        }
        catch { }
    }
}
