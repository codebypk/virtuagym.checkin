// webcam.js – Captures a single frame from the user's webcam and returns it as a base64 JPEG string.
async function webcamCapture() {
    const stream = await navigator.mediaDevices.getUserMedia({ video: true });
    const video = document.createElement('video');
    video.srcObject = stream;
    video.setAttribute('playsinline', '');
    await video.play();

    // Wait one frame for the camera to stabilise
    await new Promise(r => setTimeout(r, 500));

    const canvas = document.createElement('canvas');
    canvas.width = video.videoWidth;
    canvas.height = video.videoHeight;
    canvas.getContext('2d').drawImage(video, 0, 0);

    // Stop the camera
    stream.getTracks().forEach(t => t.stop());

    // Return base64 without the data-uri prefix
    return canvas.toDataURL('image/jpeg', 0.85).split(',')[1];
}

window.webcamCapture = webcamCapture;
