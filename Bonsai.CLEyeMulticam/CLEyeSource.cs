using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using OpenCV.Net;
using System.Threading;

namespace Bonsai.CLEyeMulticam
{
    public class CLEyeSource : Source<IplImage>
    {
        IntPtr camera;
        IplImage image;
        IplImage output;
        Thread captureThread;
        volatile bool running;
        ManualResetEventSlim stop;

        public CLEyeSource()
        {
            ColorMode = CLEyeCameraColorMode.CLEYE_COLOR_RAW;
            Resolution = CLEyeCameraResolution.CLEYE_VGA;
            FrameRate = 60;
        }

        public int CameraIndex { get; set; }

        public CLEyeCameraColorMode ColorMode { get; set; }

        public CLEyeCameraResolution Resolution { get; set; }

        public float FrameRate { get; set; }

        protected override void Start()
        {
            captureThread = new Thread(CaptureNewFrame);
            if (!CLEye.CLEyeCameraStart(camera))
            {
                throw new InvalidOperationException("Unable to start camera.");
            }

            running = true;
            captureThread.Start();
        }

        protected override void Stop()
        {
            running = false;
            stop.Wait();
            CLEye.CLEyeCameraStop(camera);
            captureThread = null;
        }

        public override IDisposable Load()
        {
            var guid = CLEye.CLEyeGetCameraUUID(CameraIndex);
            if (guid == Guid.Empty)
            {
                throw new InvalidOperationException("No camera found with the given index.");
            }

            camera = CLEye.CLEyeCreateCamera(guid, ColorMode, Resolution, FrameRate);

            int width, height;
            CLEye.CLEyeCameraGetFrameDimensions(camera, out width, out height);

            switch (ColorMode)
            {
                case CLEyeCameraColorMode.CLEYE_COLOR_RAW:
                case CLEyeCameraColorMode.CLEYE_COLOR_PROCESSED:
                    image = new IplImage(new CvSize(width, height), 8, 4);
                    output = new IplImage(image.Size, 8, 3);
                    break;
                case CLEyeCameraColorMode.CLEYE_MONO_RAW:
                case CLEyeCameraColorMode.CLEYE_MONO_PROCESSED:
                    image = new IplImage(new CvSize(width, height), 8, 1);
                    output = image;
                    break;
            }

            stop = new ManualResetEventSlim();
            return base.Load();
        }

        protected override void Unload()
        {
            CLEye.CLEyeDestroyCamera(camera);
        }

        void CaptureNewFrame()
        {
            while (running)
            {
                if (CLEye.CLEyeCameraGetFrame(camera, image.ImageData, 500))
                {
                    if (image.NumChannels == 4)
                    {
                        ImgProc.cvCvtColor(image, output, ColorConversion.BGRA2BGR);
                    }
                    Subject.OnNext(output);
                }
            }

            stop.Set();
        }
    }
}
