using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using OpenCV.Net;
using System.Threading;
using System.ComponentModel;
using System.Reactive.Linq;
using System.Reactive.Disposables;
using System.Threading.Tasks;

namespace Bonsai.CLEyeMulticam
{
    public class CLEyeSource : Source<IplImage>
    {
        readonly object captureLock = new object();
        IObservable<IplImage> source;
        IntPtr camera;
        IplImage image;
        IplImage output;

        int gain;
        int exposure;
        int whiteBalanceRed;
        int whiteBalanceGreen;
        int whiteBalanceBlue;
        bool autoGain;
        bool autoExposure;
        bool autoWhiteBalance;

        public CLEyeSource()
        {
            ColorMode = CLEyeCameraColorMode.CLEYE_COLOR_RAW;
            Resolution = CLEyeCameraResolution.CLEYE_VGA;
            FrameRate = 60;

            AutoWhiteBalance = true;
            Exposure = 511;

            source = Observable.Create<IplImage>((observer, cancellationToken) =>
            {
                return Task.Factory.StartNew(() =>
                {
                    lock (captureLock)
                    {
                        Load();
                        try
                        {
                            while (!cancellationToken.IsCancellationRequested)
                            {
                                if (CLEye.CLEyeCameraGetFrame(camera, image.ImageData, 500))
                                {
                                    if (image.Channels == 4)
                                    {
                                        CV.CvtColor(image, output, ColorConversion.Bgra2Bgr);
                                    }

                                    observer.OnNext(output.Clone());
                                }
                            }
                        }
                        finally { Unload(); }
                    }
                },
                cancellationToken,
                TaskCreationOptions.LongRunning,
                TaskScheduler.Default);
            })
            .PublishReconnectable()
            .RefCount();
        }

        [TypeConverter(typeof(CameraGuidConverter))]
        public Guid? CameraGuid { get; set; }

        public int CameraIndex { get; set; }

        public CLEyeCameraColorMode ColorMode { get; set; }

        public CLEyeCameraResolution Resolution { get; set; }

        public float FrameRate { get; set; }

        public bool AutoGain
        {
            get { return autoGain; }
            set
            {
                autoGain = value;
                if (camera != IntPtr.Zero)
                {
                    CLEye.CLEyeSetCameraParameter(camera, CLEyeCameraParameter.CLEYE_AUTO_GAIN, value ? 1 : 0);
                    if (!autoGain) Gain = gain;
                }
            }
        }

        public bool AutoExposure
        {
            get { return autoExposure; }
            set
            {
                autoExposure = value;
                if (camera != IntPtr.Zero)
                {
                    CLEye.CLEyeSetCameraParameter(camera, CLEyeCameraParameter.CLEYE_AUTO_EXPOSURE, value ? 1 : 0);
                    if (!autoExposure) Exposure = exposure;
                }
            }
        }

        public bool AutoWhiteBalance
        {
            get { return autoWhiteBalance; }
            set
            {
                autoWhiteBalance = value;
                if (camera != IntPtr.Zero)
                {
                    CLEye.CLEyeSetCameraParameter(camera, CLEyeCameraParameter.CLEYE_AUTO_WHITEBALANCE, value ? 1 : 0);
                    if (!autoWhiteBalance)
                    {
                        WhiteBalanceRed = whiteBalanceRed;
                        WhiteBalanceGreen = whiteBalanceGreen;
                        WhiteBalanceBlue = whiteBalanceBlue;
                    }
                }
            }
        }

        [Range(0, 79)]
        [Editor(DesignTypes.SliderEditor, "System.Drawing.Design.UITypeEditor, System.Drawing, Version=4.0.0.0, Culture=neutral, PublicKeyToken=b03f5f7f11d50a3a")]
        public int Gain
        {
            get { return gain; }
            set
            {
                gain = value;
                if (camera != IntPtr.Zero)
                {
                    CLEye.CLEyeSetCameraParameter(camera, CLEyeCameraParameter.CLEYE_GAIN, value);
                }
            }
        }

        [Range(0, 511)]
        [Editor(DesignTypes.SliderEditor, "System.Drawing.Design.UITypeEditor, System.Drawing, Version=4.0.0.0, Culture=neutral, PublicKeyToken=b03f5f7f11d50a3a")]
        public int Exposure
        {
            get { return exposure; }
            set
            {
                exposure = value;
                if (camera != IntPtr.Zero)
                {
                    CLEye.CLEyeSetCameraParameter(camera, CLEyeCameraParameter.CLEYE_EXPOSURE, value);
                }
            }
        }

        [Range(0, 255)]
        [Editor(DesignTypes.SliderEditor, "System.Drawing.Design.UITypeEditor, System.Drawing, Version=4.0.0.0, Culture=neutral, PublicKeyToken=b03f5f7f11d50a3a")]
        public int WhiteBalanceRed
        {
            get { return whiteBalanceRed; }
            set
            {
                whiteBalanceRed = value;
                if (camera != IntPtr.Zero)
                {
                    CLEye.CLEyeSetCameraParameter(camera, CLEyeCameraParameter.CLEYE_WHITEBALANCE_RED, value);
                }
            }
        }

        [Range(0, 255)]
        [Editor(DesignTypes.SliderEditor, "System.Drawing.Design.UITypeEditor, System.Drawing, Version=4.0.0.0, Culture=neutral, PublicKeyToken=b03f5f7f11d50a3a")]
        public int WhiteBalanceGreen
        {
            get { return whiteBalanceGreen; }
            set
            {
                whiteBalanceGreen = value;
                if (camera != IntPtr.Zero)
                {
                    CLEye.CLEyeSetCameraParameter(camera, CLEyeCameraParameter.CLEYE_WHITEBALANCE_GREEN, value);
                }
            }
        }

        [Range(0, 255)]
        [Editor(DesignTypes.SliderEditor, "System.Drawing.Design.UITypeEditor, System.Drawing, Version=4.0.0.0, Culture=neutral, PublicKeyToken=b03f5f7f11d50a3a")]
        public int WhiteBalanceBlue
        {
            get { return whiteBalanceBlue; }
            set
            {
                whiteBalanceBlue = value;
                if (camera != IntPtr.Zero)
                {
                    CLEye.CLEyeSetCameraParameter(camera, CLEyeCameraParameter.CLEYE_WHITEBALANCE_BLUE, value);
                }
            }
        }

        private void Load()
        {
            var guid = CameraGuid;
            if (!guid.HasValue)
            {
                guid = CLEye.CLEyeGetCameraUUID(CameraIndex);
                if (guid == Guid.Empty)
                {
                    throw new InvalidOperationException("No camera found with the given index.");
                }
            }

            camera = CLEye.CLEyeCreateCamera(guid.Value, ColorMode, Resolution, FrameRate);
            if (camera == IntPtr.Zero)
            {
                throw new InvalidOperationException("No camera found with the given GUID.");
            }

            AutoGain = autoGain;
            AutoExposure = autoExposure;
            AutoWhiteBalance = autoWhiteBalance;
            Gain = gain;
            Exposure = exposure;
            WhiteBalanceRed = whiteBalanceRed;
            WhiteBalanceGreen = whiteBalanceGreen;
            WhiteBalanceBlue = whiteBalanceBlue;

            int width, height;
            CLEye.CLEyeCameraGetFrameDimensions(camera, out width, out height);

            switch (ColorMode)
            {
                case CLEyeCameraColorMode.CLEYE_COLOR_RAW:
                case CLEyeCameraColorMode.CLEYE_COLOR_PROCESSED:
                    image = new IplImage(new Size(width, height), IplDepth.U8, 4);
                    output = new IplImage(image.Size, IplDepth.U8, 3);
                    break;
                case CLEyeCameraColorMode.CLEYE_MONO_RAW:
                case CLEyeCameraColorMode.CLEYE_MONO_PROCESSED:
                    image = new IplImage(new Size(width, height), IplDepth.U8, 1);
                    output = image;
                    break;
            }

            if (!CLEye.CLEyeCameraStart(camera))
            {
                throw new InvalidOperationException("Unable to start camera.");
            }
        }

        private void Unload()
        {
            CLEye.CLEyeCameraStop(camera);
            CLEye.CLEyeDestroyCamera(camera);
            camera = IntPtr.Zero;
        }

        public override IObservable<IplImage> Generate()
        {
            return source;
        }

        class CameraGuidConverter : GuidConverter
        {
            public override bool GetStandardValuesSupported(ITypeDescriptorContext context)
            {
                return true;
            }

            public override TypeConverter.StandardValuesCollection GetStandardValues(ITypeDescriptorContext context)
            {
                var cameraGuids = new List<Guid>();
                var cameraCount = CLEye.CLEyeGetCameraCount();
                for (int i = 0; i < cameraCount; i++)
                {
                    cameraGuids.Add(CLEye.CLEyeGetCameraUUID(i));
                }

                return new StandardValuesCollection(cameraGuids);
            }
        }
    }
}
