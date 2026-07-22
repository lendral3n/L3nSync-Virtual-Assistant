package com.l3n.liaVA

import android.app.Notification
import android.app.NotificationChannel
import android.app.NotificationManager
import android.app.PendingIntent
import android.app.Service
import android.content.Context
import android.content.Intent
import android.content.pm.ServiceInfo
import android.graphics.PixelFormat
import android.os.Build
import android.os.Handler
import android.os.IBinder
import android.os.Looper
import android.util.Log
import android.view.Gravity
import android.view.MotionEvent
import android.view.SurfaceView
import android.view.View
import android.view.ViewGroup
import android.view.WindowManager
import android.widget.FrameLayout
import com.unity3d.player.IUnityPlayerLifecycleEvents
import com.unity3d.player.UnityPlayerForActivityOrService
import kotlin.math.abs
import kotlin.random.Random

/**
 * Overlay Lia — jendela seukuran karakter yang BERGERAK bebas keliling layar
 * (gaya desktop-pet/Shimeji). Di luar jendela = HP normal (passthrough alami).
 *
 * Interaksi:
 *   • TAP karakter  → Lia bereaksi (UnityBridge.tapReaction)
 *   • SERET karakter → jendela pindah mengikuti jari
 *   • Roam otonom   → timer geser jendela ke titik acak tiap 15-40s;
 *                     saat bergerak Unity main animasi jalan (setLocomotion)
 *
 * Window: TYPE_APPLICATION_OVERLAY, touchable (TANPA FLAG_NOT_TOUCHABLE),
 * ukuran ~ karakter. touchCatcher (View transparan di atas Unity) menangkap sentuhan.
 */
class OverlayService : Service(), IUnityPlayerLifecycleEvents {

    companion object {
        private const val TAG = "OverlayService"
        private const val CHANNEL_ID = "lia_overlay"
        private const val NOTIFICATION_ID = 1001

        const val ACTION_START_OVERLAY = "com.l3n.liaVA.action.START_OVERLAY"
        const val ACTION_STOP_OVERLAY = "com.l3n.liaVA.action.STOP_OVERLAY"
        const val ACTION_STOP_SERVICE = "com.l3n.liaVA.action.STOP_SERVICE"

        @JvmStatic @Volatile private var instance: OverlayService? = null
    }

    private var unityPlayer: UnityPlayerForActivityOrService? = null
    private var windowManager: WindowManager? = null
    private var rootView: FrameLayout? = null
    private var params: WindowManager.LayoutParams? = null
    private var isOverlayShown = false

    private val ui = Handler(Looper.getMainLooper())

    // Ukuran jendela karakter (dp)
    private val widthDp = 240
    private val heightDp = 420

    private var screenW = 0
    private var screenH = 0
    private var winW = 0
    private var winH = 0

    override fun onBind(intent: Intent?): IBinder? = null

    override fun onCreate() {
        super.onCreate()
        instance = this
        windowManager = getSystemService(Context.WINDOW_SERVICE) as WindowManager
        createNotificationChannel()
        startForegroundWithNotification()
    }

    override fun onStartCommand(intent: Intent?, flags: Int, startId: Int): Int {
        when (intent?.action) {
            ACTION_START_OVERLAY -> showOverlay()
            ACTION_STOP_OVERLAY -> hideOverlay()
            ACTION_STOP_SERVICE -> stopServiceInternal()
            else -> showOverlay()
        }
        return START_STICKY
    }

    override fun onDestroy() {
        stopRoam()
        hideOverlay()
        unityPlayer = null
        instance = null
        super.onDestroy()
    }

    private fun measureScreen() {
        val dm = resources.displayMetrics
        screenW = dm.widthPixels
        screenH = dm.heightPixels
        winW = (widthDp * dm.density).toInt()
        winH = (heightDp * dm.density).toInt()
    }

    private fun showOverlay() {
        if (isOverlayShown) return
        measureScreen()

        if (unityPlayer == null) unityPlayer = UnityPlayerForActivityOrService(this, this)
        val playerView = unityPlayer?.frameLayout ?: run {
            Log.e(TAG, "UnityPlayer frameLayout null"); return
        }

        // Root berisi Unity view + touchCatcher transparan di atasnya
        val root = FrameLayout(this)
        root.addView(playerView, FrameLayout.LayoutParams(
            ViewGroup.LayoutParams.MATCH_PARENT, ViewGroup.LayoutParams.MATCH_PARENT))
        val touchCatcher = View(this).apply { setBackgroundColor(android.graphics.Color.TRANSPARENT) }
        root.addView(touchCatcher, FrameLayout.LayoutParams(
            ViewGroup.LayoutParams.MATCH_PARENT, ViewGroup.LayoutParams.MATCH_PARENT))

        val lp = WindowManager.LayoutParams(
            winW, winH,
            WindowManager.LayoutParams.TYPE_APPLICATION_OVERLAY,
            // Touchable (TANPA FLAG_NOT_TOUCHABLE). NOT_FOCUSABLE supaya keyboard app lain jalan.
            WindowManager.LayoutParams.FLAG_NOT_FOCUSABLE or
                WindowManager.LayoutParams.FLAG_LAYOUT_NO_LIMITS or
                WindowManager.LayoutParams.FLAG_HARDWARE_ACCELERATED,
            PixelFormat.TRANSLUCENT
        ).apply {
            gravity = Gravity.TOP or Gravity.START
            // Posisi awal: bawah-tengah
            x = (screenW - winW) / 2
            y = screenH - winH - (48 * resources.displayMetrics.density).toInt()
        }
        params = lp

        try {
            windowManager?.addView(root, lp)
            rootView = root
            isOverlayShown = true
        } catch (e: Exception) {
            Log.e(TAG, "Failed addView: ${e.message}"); return
        }

        if (playerView is ViewGroup) makeUnitySurfaceTransparent(playerView)
        forceMatchParentRecursive(playerView)

        attachTouch(touchCatcher, lp)

        playerView.requestFocus()
        unityPlayer?.windowFocusChanged(true)
        unityPlayer?.resume()

        // Mulai roam otonom
        scheduleNextRoam()
        Log.d(TAG, "Overlay MOVABLE shown: ${winW}x${winH} at (${lp.x},${lp.y})")
    }

    // ---- Touch: tap vs drag ----
    private var downX = 0f; private var downY = 0f
    private var startWinX = 0; private var startWinY = 0
    private var dragging = false
    private val touchSlopPx by lazy { (8 * resources.displayMetrics.density) }

    private fun attachTouch(catcher: View, lp: WindowManager.LayoutParams) {
        catcher.setOnTouchListener { _, ev ->
            when (ev.action) {
                MotionEvent.ACTION_DOWN -> {
                    downX = ev.rawX; downY = ev.rawY
                    startWinX = lp.x; startWinY = lp.y
                    dragging = false
                    stopRoam() // user pegang → hentikan roam otomatis
                    true
                }
                MotionEvent.ACTION_MOVE -> {
                    val dx = ev.rawX - downX; val dy = ev.rawY - downY
                    if (!dragging && (abs(dx) > touchSlopPx || abs(dy) > touchSlopPx)) dragging = true
                    if (dragging) {
                        lp.x = (startWinX + dx).toInt().coerceIn(-winW / 3, screenW - winW * 2 / 3)
                        lp.y = (startWinY + dy).toInt().coerceIn(0, screenH - winH / 2)
                        try { windowManager?.updateViewLayout(rootView, lp) } catch (_: Exception) {}
                    }
                    true
                }
                MotionEvent.ACTION_UP, MotionEvent.ACTION_CANCEL -> {
                    if (!dragging) UnityBridge.tapReaction()  // TAP → Lia bereaksi
                    scheduleNextRoam()                        // lanjut roam lagi
                    true
                }
                else -> false
            }
        }
    }

    // ---- Roam otonom: geser jendela ke titik acak ----
    private var roamRunnable: Runnable? = null
    private var stepRunnable: Runnable? = null

    private fun scheduleNextRoam() {
        stopRoam()
        val delay = Random.nextLong(15_000, 40_000)
        roamRunnable = Runnable { roamToRandom() }.also { ui.postDelayed(it, delay) }
    }

    private fun stopRoam() {
        roamRunnable?.let { ui.removeCallbacks(it) }; roamRunnable = null
        stepRunnable?.let { ui.removeCallbacks(it) }; stepRunnable = null
    }

    private fun roamToRandom() {
        val lp = params ?: return
        val targetX = Random.nextInt(0, (screenW - winW).coerceAtLeast(1))
        val minY = (screenH * 0.25f).toInt()
        val maxY = (screenH - winH).coerceAtLeast(minY + 1)
        val targetY = Random.nextInt(minY, maxY)
        val startX = lp.x; val startY = lp.y
        val steps = 48
        var i = 0
        UnityBridge.setLocomotion(true) // mulai jalan → animasi FemWalk

        val stepper = object : Runnable {
            override fun run() {
                i++
                val t = i / steps.toFloat()
                val e = t * t * (3f - 2f * t) // smoothstep
                lp.x = (startX + (targetX - startX) * e).toInt()
                lp.y = (startY + (targetY - startY) * e).toInt()
                try { windowManager?.updateViewLayout(rootView, lp) } catch (_: Exception) {}
                if (i < steps) {
                    stepRunnable = this; ui.postDelayed(this, 16)
                } else {
                    UnityBridge.setLocomotion(false) // sampai → idle
                    scheduleNextRoam()
                }
            }
        }
        stepRunnable = stepper
        ui.postDelayed(stepper, 16)
    }

    private fun hideOverlay() {
        if (!isOverlayShown) return
        stopRoam()
        try { rootView?.let { windowManager?.removeView(it) } } catch (e: Exception) {
            Log.w(TAG, "removeView error: ${e.message}")
        }
        rootView = null
        isOverlayShown = false
        unityPlayer?.pause()
    }

    @Volatile private var isStoppingService = false

    private fun stopServiceInternal() {
        if (isStoppingService) return
        isStoppingService = true
        hideOverlay()
        stopForeground(STOP_FOREGROUND_REMOVE)
        stopSelf()
    }

    private fun forceMatchParentRecursive(view: View) {
        val lp = view.layoutParams
        if (lp != null) {
            lp.width = ViewGroup.LayoutParams.MATCH_PARENT
            lp.height = ViewGroup.LayoutParams.MATCH_PARENT
            view.layoutParams = lp
        }
        if (view is ViewGroup) for (i in 0 until view.childCount) forceMatchParentRecursive(view.getChildAt(i))
    }

    private fun makeUnitySurfaceTransparent(parent: ViewGroup) {
        for (i in 0 until parent.childCount) {
            when (val child = parent.getChildAt(i)) {
                is SurfaceView -> {
                    child.setZOrderOnTop(true)
                    child.holder.setFormat(PixelFormat.TRANSLUCENT)
                    child.setBackgroundColor(android.graphics.Color.TRANSPARENT)
                }
                is ViewGroup -> makeUnitySurfaceTransparent(child)
            }
        }
    }

    private fun createNotificationChannel() {
        val nm = getSystemService(NotificationManager::class.java)
        val channel = NotificationChannel(
            CHANNEL_ID, getString(R.string.overlay_channel_name),
            NotificationManager.IMPORTANCE_LOW
        ).apply {
            description = getString(R.string.overlay_channel_description)
            setShowBadge(false)
        }
        nm.createNotificationChannel(channel)
    }

    private fun startForegroundWithNotification() {
        val pendingIntent = PendingIntent.getActivity(
            this, 0,
            Intent(this, MainActivity::class.java).apply {
                flags = Intent.FLAG_ACTIVITY_NEW_TASK or Intent.FLAG_ACTIVITY_SINGLE_TOP
            },
            PendingIntent.FLAG_IMMUTABLE
        )
        val stopServiceIntent = PendingIntent.getService(
            this, 1,
            Intent(this, OverlayService::class.java).setAction(ACTION_STOP_SERVICE),
            PendingIntent.FLAG_IMMUTABLE
        )
        val notification: Notification = Notification.Builder(this, CHANNEL_ID)
            .setContentTitle(getString(R.string.overlay_notification_title))
            .setContentText(getString(R.string.overlay_notification_text))
            .setSmallIcon(android.R.drawable.ic_menu_view)
            .setContentIntent(pendingIntent)
            .addAction(android.R.drawable.ic_menu_close_clear_cancel, "Tutup", stopServiceIntent)
            .setOngoing(true)
            .build()
        if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.UPSIDE_DOWN_CAKE) {
            startForeground(NOTIFICATION_ID, notification, ServiceInfo.FOREGROUND_SERVICE_TYPE_SPECIAL_USE)
        } else {
            startForeground(NOTIFICATION_ID, notification)
        }
    }

    override fun onUnityPlayerUnloaded() { Log.d(TAG, "Unity player unloaded") }
    override fun onUnityPlayerQuitted() {
        Log.d(TAG, "Unity player quitted")
        if (!isStoppingService) stopServiceInternal()
    }
}
