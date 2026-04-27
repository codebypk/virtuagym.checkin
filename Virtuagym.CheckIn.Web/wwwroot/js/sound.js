// Sound playback via JS Interop for Blazor Server
(function () {
    var unlocked = false;
    var pendingUrls = [];
    var soundBlocked = true;
    var soundBlockStateCallback = null;

    function enqueue(url) {
        if (!url)
            return;

        // Keep queue small and avoid endless duplicates.
        if (pendingUrls.length > 20)
            pendingUrls.shift();
        pendingUrls.push(url);
    }

    function notifySoundBlockedState(isBlocked) {
        soundBlocked = isBlocked;

        if (!soundBlockStateCallback)
            return;

        try {
            soundBlockStateCallback.invokeMethodAsync('SetSoundBlocked', isBlocked);
        } catch {
            // ignored
        }
    }

    function playNow(url) {
        try {
            var audio = new Audio(url);
            audio.play().then(function () {
                if (soundBlocked)
                    notifySoundBlockedState(false);
            }).catch(function (err) {
                // If browser still blocks it, re-queue and wait for next user gesture.
                enqueue(url);
                notifySoundBlockedState(true);
                console.warn('Sound playback blocked:', err.message);
            });
        } catch (e) {
            console.warn('Sound error:', e.message);
        }
    }

    function flushPending() {
        if (!unlocked || pendingUrls.length === 0)
            return;

        var urls = pendingUrls.slice();
        pendingUrls = [];
        urls.forEach(function (url) {
            playNow(url);
        });
    }

    function detachUnlockListeners() {
        document.removeEventListener('pointerdown', unlockFromUserGesture, true);
        document.removeEventListener('click', unlockFromUserGesture, true);
        document.removeEventListener('touchstart', unlockFromUserGesture, true);
        document.removeEventListener('keydown', unlockFromUserGesture, true);
    }

    function primeAudioFromGesture() {
        try {
            var audio = new Audio();
            audio.muted = true;
            audio.playsInline = true;
            audio.src = 'data:audio/wav;base64,UklGRiQAAABXQVZFZm10IBAAAAABAAEAESsAACJWAAACABAAZGF0YQAAAAA=';

            var playPromise = audio.play();
            if (playPromise && typeof playPromise.then === 'function') {
                return playPromise.then(function () {
                    audio.pause();
                    audio.currentTime = 0;
                    return true;
                }).catch(function () {
                    return false;
                });
            }

            return Promise.resolve(true);
        } catch {
            return Promise.resolve(false);
        }
    }

    async function unlockFromUserGesture() {
        if (unlocked)
            return;

        var primed = await primeAudioFromGesture();
        if (!primed) {
            notifySoundBlockedState(true);
            return;
        }

        unlocked = true;
        notifySoundBlockedState(false);
        flushPending();
        detachUnlockListeners();
    }

    // Capture phase to maximize chance of receiving first interaction.
    document.addEventListener('pointerdown', unlockFromUserGesture, true);
    document.addEventListener('click', unlockFromUserGesture, true);
    document.addEventListener('touchstart', unlockFromUserGesture, true);
    document.addEventListener('keydown', unlockFromUserGesture, true);

    // Optional explicit unlock hook (can be called from a button if needed).
    window.enableSound = function () {
        unlockFromUserGesture();
    };

    window.registerSoundBlockStateCallback = function (dotNetRef) {
        soundBlockStateCallback = dotNetRef;
        notifySoundBlockedState(soundBlocked);
    };

    window.isSoundBlocked = function () {
        return soundBlocked;
    };

    window.playSound = function (url) {
        if (!url)
            return;

        if (!unlocked) {
            enqueue(url);
            return;
        }

        playNow(url);
    };
})();
