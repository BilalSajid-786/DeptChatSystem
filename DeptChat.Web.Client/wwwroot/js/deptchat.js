// Browser helpers for the DeptChat web app (called from C# via IJSRuntime).
// Keep this file small and free of app logic: storage, scrolling, audio, clipboard, notifications.
window.deptchat = (function () {
    "use strict";

    function safe(fn, fallback) {
        try { return fn(); } catch (e) { return fallback; }
    }

    // ---------- storage ----------
    const storage = {
        load: (key) => safe(() => localStorage.getItem(key) ?? sessionStorage.getItem(key), null),
        save: (key, value, persistent) => safe(() => {
            const keep = persistent ? localStorage : sessionStorage;
            const drop = persistent ? sessionStorage : localStorage;
            keep.setItem(key, value);
            drop.removeItem(key);
            return true;
        }, false),
        saveLocal: (key, value) => safe(() => { localStorage.setItem(key, value); return true; }, false),
        loadLocal: (key) => safe(() => localStorage.getItem(key), null),
        isPersistent: (key) => safe(() => localStorage.getItem(key) !== null, false),
        remove: (key) => {
            safe(() => localStorage.removeItem(key));
            safe(() => sessionStorage.removeItem(key));
        },
        // Calls back when another tab signs out (the key is removed from localStorage).
        watch: (ref, key) => {
            window.addEventListener("storage", (e) => {
                if (e.key === key && e.newValue === null) {
                    ref.invokeMethodAsync("OnSignedOutElsewhere");
                }
            });
        }
    };

    function deviceId() {
        return safe(() => {
            let id = localStorage.getItem("deptchat_device_id");
            if (!id) {
                id = (crypto.randomUUID ? crypto.randomUUID() : String(Date.now()) + Math.random().toString(16).slice(2));
                localStorage.setItem("deptchat_device_id", id);
            }
            return id;
        }, "web-unknown");
    }

    function deviceName() {
        const ua = navigator.userAgent || "";
        let browser = "Browser";
        if (/Edg\//.test(ua)) browser = "Edge";
        else if (/OPR\//.test(ua)) browser = "Opera";
        else if (/Firefox\//.test(ua)) browser = "Firefox";
        else if (/Chrome\//.test(ua)) browser = "Chrome";
        else if (/Safari\//.test(ua)) browser = "Safari";
        let os = "";
        if (/Windows/.test(ua)) os = "Windows";
        else if (/Android/.test(ua)) os = "Android";
        else if (/iPhone|iPad|iPod/.test(ua)) os = "iOS";
        else if (/Mac OS X/.test(ua)) os = "macOS";
        else if (/Linux/.test(ua)) os = "Linux";
        return os ? browser + " on " + os : browser;
    }

    // ---------- scrolling ----------
    const scroll = {
        // Pinned bar: bring one message into view and flash it briefly. Returns false if it isn't loaded.
        toElement: (id) => {
            const target = document.getElementById(id);
            if (!target) return false;
            target.scrollIntoView({ block: "center", behavior: "smooth" });
            target.classList.add("flash");
            setTimeout(() => target.classList.remove("flash"), 1600);
            return true;
        },
        // Photos finish loading after the first scroll and push the last message out of view,
        // so stay pinned to the bottom as each still-loading image arrives (unless the user scrolled up).
        toBottom: (el) => {
            if (!el) return;
            el.scrollTop = el.scrollHeight;
            el.querySelectorAll("img").forEach((img) => {
                if (img.complete) return;
                img.addEventListener("load", () => {
                    if ((el.scrollHeight - el.scrollTop - el.clientHeight) < img.clientHeight + 160) el.scrollTop = el.scrollHeight;
                }, { once: true });
            });
        },
        isNearBottom: (el) => el ? (el.scrollHeight - el.scrollTop - el.clientHeight) < 120 : true,
        height: (el) => el ? el.scrollHeight : 0,
        // After older messages are prepended, keep the same messages in view.
        keepPosition: (el, oldHeight) => { if (el) el.scrollTop = el.scrollHeight - oldHeight + el.scrollTop; },
        watch: (el, ref) => {
            if (!el) return;
            let ticking = false;
            el.addEventListener("scroll", () => {
                if (ticking) return;
                ticking = true;
                requestAnimationFrame(() => {
                    ticking = false;
                    const nearTop = el.scrollTop < 80;
                    const nearBottom = (el.scrollHeight - el.scrollTop - el.clientHeight) < 120;
                    ref.invokeMethodAsync("OnScrollChanged", nearTop, nearBottom);
                });
            }, { passive: true });
        }
    };

    // ---------- page visibility ----------
    const visibility = {
        isVisible: () => !document.hidden,
        watch: (ref) => {
            document.addEventListener("visibilitychange", () => {
                ref.invokeMethodAsync("OnVisibilityChanged", !document.hidden);
            });
        }
    };

    // ---------- composer ----------
    const composer = {
        // Enter sends, Shift+Enter makes a new line; the textarea grows with its content.
        attach: (el, ref) => {
            if (!el || el.__deptchatAttached) return;
            el.__deptchatAttached = true;
            const grow = () => { el.style.height = "auto"; el.style.height = Math.min(el.scrollHeight, 160) + "px"; };
            el.addEventListener("input", grow);
            el.addEventListener("keydown", (e) => {
                if (e.key === "Enter" && !e.shiftKey && !e.isComposing) {
                    e.preventDefault();
                    ref.invokeMethodAsync("OnEnterPressed", el.value);
                }
            });
            el.addEventListener("paste", (e) => {
                const items = e.clipboardData ? e.clipboardData.files : null;
                if (items && items.length > 0) {
                    const input = document.getElementById(el.dataset.fileInput);
                    if (input) {
                        const dt = new DataTransfer();
                        dt.items.add(items[0]);
                        input.files = dt.files;
                        input.dispatchEvent(new Event("change", { bubbles: true }));
                        e.preventDefault();
                    }
                }
            });
            grow();
        },
        reset: (el) => { if (el) { el.value = ""; el.style.height = "auto"; } },
        focus: (el) => { if (el) el.focus(); }
    };

    // Drag-and-drop of a file onto the chat forwards it to the hidden file input.
    function attachDrop(zone, inputId) {
        if (!zone || zone.__deptchatDrop) return;
        zone.__deptchatDrop = true;
        zone.addEventListener("dragover", (e) => { e.preventDefault(); zone.classList.add("drop-active"); });
        zone.addEventListener("dragleave", () => zone.classList.remove("drop-active"));
        zone.addEventListener("drop", (e) => {
            e.preventDefault();
            zone.classList.remove("drop-active");
            const files = e.dataTransfer ? e.dataTransfer.files : null;
            const input = document.getElementById(inputId);
            if (files && files.length > 0 && input) {
                const dt = new DataTransfer();
                dt.items.add(files[0]);
                input.files = dt.files;
                input.dispatchEvent(new Event("change", { bubbles: true }));
            }
        });
    }

    // ---------- clipboard & notifications ----------
    async function copy(text) {
        try { await navigator.clipboard.writeText(text); return true; } catch (e) { return false; }
    }

    const notify = {
        permission: () => ("Notification" in window) ? Notification.permission : "unsupported",
        request: async () => ("Notification" in window) ? await Notification.requestPermission() : "unsupported",
        show: (title, body) => {
            if (!("Notification" in window) || Notification.permission !== "granted" || !document.hidden) return;
            try { new Notification(title, { body: body, icon: "favicon.svg" }); } catch (e) { }
        }
    };

    // ---------- voice recording ----------
    // The Android app only plays "[voice:data:audio/m4a;base64,...]", so record AAC in MP4 only.
    const RECORD_TYPES = ["audio/mp4;codecs=mp4a.40.2", "audio/mp4"];
    let rec = null;

    function recordType() {
        if (!window.MediaRecorder || !navigator.mediaDevices) return null;
        for (const t of RECORD_TYPES) {
            if (MediaRecorder.isTypeSupported(t)) return t;
        }
        return null;
    }

    const voice = {
        isSupported: () => recordType() !== null,
        start: async () => {
            const type = recordType();
            if (!type) return "unsupported";
            let stream;
            try {
                stream = await navigator.mediaDevices.getUserMedia({ audio: true });
            } catch (e) {
                return "denied";
            }
            const chunks = [];
            const recorder = new MediaRecorder(stream, { mimeType: type, audioBitsPerSecond: 64000 });
            recorder.ondataavailable = (e) => { if (e.data && e.data.size > 0) chunks.push(e.data); };
            recorder.start(250);
            rec = { recorder, stream, chunks, startedAt: Date.now(), elapsedBefore: 0, type };
            return "ok";
        },
        elapsed: () => {
            if (!rec) return 0;
            const running = rec.recorder.state === "recording" ? (Date.now() - rec.startedAt) : 0;
            return Math.round((rec.elapsedBefore + running) / 1000);
        },
        // Stops recording; returns { base64, seconds, previewUrl } or null.
        stop: () => new Promise((resolve) => {
            if (!rec) { resolve(null); return; }
            const r = rec;
            const finish = () => {
                const seconds = Math.max(1, Math.round((r.elapsedBefore + (r.stoppedAt - r.startedAt)) / 1000));
                r.stream.getTracks().forEach((t) => t.stop());
                const blob = new Blob(r.chunks, { type: "audio/mp4" });
                const reader = new FileReader();
                reader.onloadend = () => {
                    const dataUrl = String(reader.result || "");
                    const comma = dataUrl.indexOf(",");
                    resolve({ base64: comma >= 0 ? dataUrl.substring(comma + 1) : "", seconds: seconds, previewUrl: URL.createObjectURL(blob) });
                };
                reader.readAsDataURL(blob);
            };
            r.stoppedAt = Date.now();
            if (r.recorder.state === "inactive") { finish(); }
            else { r.recorder.onstop = finish; r.recorder.stop(); }
            rec = null;
        }),
        cancel: () => {
            if (!rec) return;
            try { rec.recorder.stop(); } catch (e) { }
            rec.stream.getTracks().forEach((t) => t.stop());
            rec = null;
        },
        revoke: (url) => { if (url) safe(() => URL.revokeObjectURL(url)); }
    };

    // ---------- audio playback (one voice note at a time) ----------
    let player = null;
    let playerRef = null;
    let playerId = null;

    const audio = {
        play: (ref, id, src, rate) => {
            if (playerId !== id) {
                audio.stop();
                // Mobile stores "audio/m4a"; browsers recognise "audio/mp4" for the same data.
                const fixed = src.replace(/^data:audio\/m4a;/i, "data:audio/mp4;");
                player = new Audio(fixed);
                playerId = id;
                playerRef = ref;
                player.ontimeupdate = () => playerRef && playerRef.invokeMethodAsync("OnAudioProgress", playerId, player.currentTime, isFinite(player.duration) ? player.duration : 0);
                player.onended = () => { const r = playerRef, i = playerId; audio.stop(); r && r.invokeMethodAsync("OnAudioEnded", i); };
                player.onerror = () => { const r = playerRef, i = playerId; audio.stop(); r && r.invokeMethodAsync("OnAudioError", i); };
            }
            player.playbackRate = rate || 1;
            player.play().catch(() => { const r = playerRef, i = playerId; audio.stop(); r && r.invokeMethodAsync("OnAudioError", i); });
        },
        pause: () => { if (player) player.pause(); },
        setRate: (rate) => { if (player) player.playbackRate = rate; },
        stop: () => {
            if (player) {
                player.ontimeupdate = null; player.onended = null; player.onerror = null;
                try { player.pause(); } catch (e) { }
            }
            player = null; playerId = null; playerRef = null;
        }
    };

    return { storage, deviceId, deviceName, scroll, visibility, composer, attachDrop, copy, notify, voice, audio };
})();
