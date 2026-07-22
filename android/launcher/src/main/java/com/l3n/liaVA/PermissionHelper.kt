package com.l3n.liaVA

import android.content.Context
import android.content.Intent
import android.net.Uri
import android.os.Build
import android.os.PowerManager
import android.provider.Settings

/**
 * Helper untuk cek + request 3 permission yang dibutuhkan floating overlay di Android (terutama MIUI/HyperOS):
 * - SYSTEM_ALERT_WINDOW (overlay)
 * - REQUEST_IGNORE_BATTERY_OPTIMIZATIONS (battery whitelist)
 * - MIUI Autostart (proprietary)
 */
object PermissionHelper {

    /** Cek apakah app punya permission overlay (Settings.canDrawOverlays). */
    fun hasOverlayPermission(context: Context): Boolean = Settings.canDrawOverlays(context)

    /** Buat intent untuk Settings page > "Display over other apps" untuk app ini. */
    fun overlayPermissionIntent(context: Context): Intent =
        Intent(
            Settings.ACTION_MANAGE_OVERLAY_PERMISSION,
            Uri.parse("package:${context.packageName}")
        )

    /** Cek apakah app sudah di-whitelist battery optimization. */
    fun isBatteryWhitelisted(context: Context): Boolean {
        val pm = context.getSystemService(Context.POWER_SERVICE) as PowerManager
        return pm.isIgnoringBatteryOptimizations(context.packageName)
    }

    /** Buat intent request battery whitelist (Android 6+). */
    fun batteryWhitelistIntent(context: Context): Intent =
        Intent(Settings.ACTION_REQUEST_IGNORE_BATTERY_OPTIMIZATIONS).apply {
            data = Uri.parse("package:${context.packageName}")
        }

    /**
     * Cek apakah running di Xiaomi/MIUI/HyperOS — perlu autostart permission terpisah.
     * Diagnose pakai property ro.miui.ui.version.name atau brand check.
     */
    fun isMiui(): Boolean {
        if (!Build.MANUFACTURER.equals("Xiaomi", ignoreCase = true) &&
            !Build.BRAND.equals("Xiaomi", ignoreCase = true) &&
            !Build.BRAND.equals("Redmi", ignoreCase = true)
        ) return false

        return try {
            val process = Runtime.getRuntime().exec("getprop ro.miui.ui.version.name")
            val version = process.inputStream.bufferedReader().readText().trim()
            version.isNotEmpty()
        } catch (_: Exception) {
            true // assume MIUI kalau brand Xiaomi
        }
    }

    /**
     * Buat intent ke MIUI Autostart settings page.
     * Path setting bisa berbeda antar versi MIUI; ini target MIUI 12+/HyperOS.
     * Kalau gagal, fallback ke app info detail.
     */
    fun miuiAutostartIntent(context: Context): Intent {
        val intent = Intent()
        intent.component = android.content.ComponentName(
            "com.miui.securitycenter",
            "com.miui.permcenter.autostart.AutoStartManagementActivity"
        )
        // Kalau MIUI activity tidak ada, return app info sebagai fallback
        return if (context.packageManager.resolveActivity(intent, 0) != null) {
            intent
        } else {
            Intent(Settings.ACTION_APPLICATION_DETAILS_SETTINGS).apply {
                data = Uri.parse("package:${context.packageName}")
            }
        }
    }
}
