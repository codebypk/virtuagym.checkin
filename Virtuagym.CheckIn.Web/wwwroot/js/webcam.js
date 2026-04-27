// webcam.js – preview + capture helpers for access pass webcam dialog.
const webcamState = {
    stream: null,
    deviceId: null,
    videoElement: null
};

function ensureWebcamApi() {
    if (!navigator.mediaDevices || !navigator.mediaDevices.getUserMedia) {
        throw new Error('Webcam API is not available in this browser context.');
    }
}

async function webcamGetDevices() {
    ensureWebcamApi();

    // Warm-up permission so labels are available on many browsers.
    let warmupStream = null;
    try {
        warmupStream = await navigator.mediaDevices.getUserMedia({ video: true });
    } catch {
        // ignore - enumerateDevices may still return devices
    } finally {
        if (warmupStream) {
            warmupStream.getTracks().forEach(t => t.stop());
        }
    }

    const devices = await navigator.mediaDevices.enumerateDevices();
    const cameras = devices.filter(d => d.kind === 'videoinput');

    return cameras.map((d, i) => ({
        deviceId: d.deviceId,
        label: d.label && d.label.trim().length > 0 ? d.label : `Kamera ${i + 1}`
    }));
}

async function webcamStartPreview(videoElementOrRef, deviceId) {
    ensureWebcamApi();
    const videoElement = resolveElement(videoElementOrRef);
    if (!videoElement) {
        throw new Error('Video element is missing.');
    }

    await webcamStopPreview(videoElement);

    const selectedDeviceId = deviceId && deviceId.trim().length > 0 ? deviceId.trim() : null;
    const baseVideo = {
        width: { ideal: 1280 },
        height: { ideal: 720 }
    };

    const attempts = selectedDeviceId
        ? [
            { video: { ...baseVideo, deviceId: { exact: selectedDeviceId } } },
            { video: { ...baseVideo, deviceId: { ideal: selectedDeviceId } } },
            { video: { ...baseVideo, facingMode: 'user' } },
            { video: true }
        ]
        : [
            { video: { ...baseVideo, facingMode: 'user' } },
            { video: true }
        ];

    let stream = null;
    const errors = [];

    for (const constraints of attempts) {
        try {
            stream = await navigator.mediaDevices.getUserMedia(constraints);
            break;
        } catch (err) {
            const message = err && err.message ? err.message : String(err);
            errors.push(message);
        }
    }

    if (!stream) {
        throw new Error(`Could not start video source: ${errors.join(' | ')}`);
    }

    webcamState.stream = stream;
    webcamState.deviceId = selectedDeviceId;
    webcamState.videoElement = videoElement;

    videoElement.setAttribute('playsinline', '');
    videoElement.muted = true;
    videoElement.srcObject = stream;

    await new Promise((resolve, reject) => {
        const timeout = setTimeout(() => reject(new Error('Webcam startup timed out.')), 5000);
        videoElement.onloadedmetadata = () => {
            clearTimeout(timeout);
            resolve();
        };
        videoElement.onerror = () => {
            clearTimeout(timeout);
            reject(new Error('Failed to load webcam metadata.'));
        };
    });

    await videoElement.play();
}

// Helper: resolve Blazor ElementReference or direct DOM element.
function resolveElement(ref) {
    if (ref instanceof HTMLElement) return ref;
    if (ref && ref.__internalId) return document.querySelector(`[_bl_${ref.__internalId}]`);
    if (ref && typeof ref === 'object') {
        // Blazor serializes ElementReference as { "__internalId": "..." }
        const id = ref.__internalId || ref['__internalId'];
        if (id) return document.querySelector(`[_bl_${id}]`);
    }
    return ref;
}

function webcamCaptureFrame(videoElementOrRef) {
    const videoElement = webcamState.videoElement || resolveElement(videoElementOrRef);
    if (!videoElement || typeof videoElement.videoWidth === 'undefined') {
        throw new Error('Video element is missing or not a valid HTML video element.');
    }

    const width = videoElement.videoWidth;
    const height = videoElement.videoHeight;

    if (!width || !height) {
        throw new Error('Video has no dimensions yet – is the camera stream active?');
    }

    const canvas = document.createElement('canvas');
    canvas.width = width;
    canvas.height = height;

    const context = canvas.getContext('2d');
    if (!context) {
        throw new Error('Unable to create 2D canvas context.');
    }

    context.drawImage(videoElement, 0, 0, width, height);
    const dataUri = canvas.toDataURL('image/jpeg', 0.9);
    return dataUri.split(',')[1] ?? null;
}

async function webcamStopPreview(videoElementOrRef) {
    if (webcamState.stream) {
        webcamState.stream.getTracks().forEach(t => t.stop());
        webcamState.stream = null;
        webcamState.deviceId = null;
        webcamState.videoElement = null;
    }

    const videoElement = resolveElement(videoElementOrRef);
    if (videoElement && videoElement.srcObject) {
        videoElement.pause?.();
        videoElement.srcObject = null;
    }
}

// Backward compatibility for older callers.
async function webcamCapture() {
    ensureWebcamApi();
    const stream = await navigator.mediaDevices.getUserMedia({ video: true });
    try {
        const video = document.createElement('video');
        video.srcObject = stream;
        video.setAttribute('playsinline', '');
        video.muted = true;
        await new Promise((resolve, reject) => {
            const timeout = setTimeout(() => reject(new Error('Webcam startup timed out.')), 5000);
            video.onloadedmetadata = () => {
                clearTimeout(timeout);
                resolve();
            };
            video.onerror = () => {
                clearTimeout(timeout);
                reject(new Error('Failed to load webcam metadata.'));
            };
        });
        await video.play();
        return webcamCaptureFrame(video);
    } finally {
        stream.getTracks().forEach(t => t.stop());
    }
}

function webcamCaptureFrameCallback(dotNetHelper) {
    try {
        const videoElement = webcamState.videoElement;
        console.log('[webcam] captureFrameCallback: videoElement=', videoElement, 'videoWidth=', videoElement?.videoWidth);
        if (!videoElement || !videoElement.videoWidth || !videoElement.videoHeight) {
            console.warn('[webcam] No video element or no dimensions, calling back with empty string');
            dotNetHelper.invokeMethodAsync('OnWebcamCaptureResult', '');
            return;
        }
        const canvas = document.createElement('canvas');
        canvas.width = videoElement.videoWidth;
        canvas.height = videoElement.videoHeight;
        const ctx = canvas.getContext('2d');
        ctx.drawImage(videoElement, 0, 0, canvas.width, canvas.height);
        const base64 = canvas.toDataURL('image/jpeg', 0.9).split(',')[1] || '';
        console.log('[webcam] Captured frame, base64 length:', base64.length);
        dotNetHelper.invokeMethodAsync('OnWebcamCaptureResult', base64);
    } catch (e) {
        console.error('[webcam] captureFrameCallback error:', e);
        dotNetHelper.invokeMethodAsync('OnWebcamCaptureResult', '');
    }
}

window.webcamGetDevices = webcamGetDevices;
window.webcamStartPreview = webcamStartPreview;
window.webcamCaptureFrame = webcamCaptureFrame;
window.webcamCaptureFrameCallback = webcamCaptureFrameCallback;
window.webcamStopPreview = webcamStopPreview;
window.webcamCapture = webcamCapture;
