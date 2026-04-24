// Sound playback via JS Interop for Blazor Server
window.playSound = function (url) {
    try {
        var audio = new Audio(url);
        audio.play().catch(function (err) {
            console.warn('Sound playback blocked:', err.message);
        });
    } catch (e) {
        console.warn('Sound error:', e.message);
    }
};
