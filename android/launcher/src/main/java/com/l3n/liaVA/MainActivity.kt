package com.l3n.liaVA

import android.content.Intent
import android.os.Bundle
import androidx.activity.ComponentActivity
import androidx.activity.compose.rememberLauncherForActivityResult
import androidx.activity.compose.setContent
import androidx.activity.result.contract.ActivityResultContracts
import androidx.compose.animation.AnimatedVisibility
import androidx.compose.foundation.background
import androidx.compose.foundation.border
import androidx.compose.foundation.clickable
import androidx.compose.foundation.layout.*
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.shape.CircleShape
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.foundation.verticalScroll
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.filled.Close
import androidx.compose.material.icons.filled.Settings
import androidx.compose.material3.*
import androidx.compose.runtime.*
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.graphics.Brush
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import androidx.core.content.ContextCompat
import com.l3n.liaVA.ai.AiPrefs
import com.l3n.liaVA.ai.LiaBrain
import com.l3n.liaVA.ui.ChatScreen
import com.l3n.liaVA.ui.theme.LiaVATheme

class MainActivity : ComponentActivity() {

    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)
        setContent {
            LiaVATheme {
                val context = LocalContext.current
                val prefs = remember { AiPrefs(context) }
                val brain = remember { LiaBrain(context, prefs) }
                var showChat by remember { mutableStateOf(false) }

                Surface(modifier = Modifier.fillMaxSize()) {
                    if (showChat) {
                        ChatScreen(brain = brain, onBack = { showChat = false })
                    } else {
                        HomeScreen(
                            prefs = prefs,
                            onStart = ::startOverlayService,
                            onStop = ::stopOverlay,
                            onOpenChat = { showChat = true },
                        )
                    }
                }
            }
        }
    }

    private fun startOverlayService() {
        val intent = Intent(this, OverlayService::class.java).setAction(OverlayService.ACTION_START_OVERLAY)
        ContextCompat.startForegroundService(this, intent)
    }

    private fun stopOverlay() {
        startService(Intent(this, OverlayService::class.java).setAction(OverlayService.ACTION_STOP_OVERLAY))
    }
}

private val CharDress = "dress"
private val CharKimono = "kimono"

@Composable
private fun HomeScreen(
    prefs: AiPrefs,
    onStart: () -> Unit,
    onStop: () -> Unit,
    onOpenChat: () -> Unit,
) {
    val context = LocalContext.current

    var permRefresh by remember { mutableIntStateOf(0) }
    val hasOverlay = remember(permRefresh) { PermissionHelper.hasOverlayPermission(context) }
    val hasBattery = remember(permRefresh) { PermissionHelper.isBatteryWhitelisted(context) }
    val isMiui = remember { PermissionHelper.isMiui() }
    val canStart = hasOverlay

    val overlayLauncher = rememberLauncherForActivityResult(
        ActivityResultContracts.StartActivityForResult()) { permRefresh++ }
    val batteryLauncher = rememberLauncherForActivityResult(
        ActivityResultContracts.StartActivityForResult()) { permRefresh++ }

    var liaActive by remember { mutableStateOf(false) }
    var selectedChar by remember { mutableStateOf(CharKimono) }
    var showSettings by remember { mutableStateOf(false) }

    // Background lembut cool (nuansa kimono putih-biru)
    val bg = Brush.verticalGradient(
        listOf(Color(0xFFEDF2FB), Color(0xFFE3EAF6), Color(0xFFDCE4F5))
    )
    val bgDark = Brush.verticalGradient(
        listOf(Color(0xFF15171E), Color(0xFF1B1F2A), Color(0xFF171A22))
    )
    val dark = isSystemDark()

    Box(Modifier.fillMaxSize().background(if (dark) bgDark else bg)) {
        Column(
            Modifier.fillMaxSize().verticalScroll(rememberScrollState())
                .padding(horizontal = 20.dp, vertical = 16.dp),
            horizontalAlignment = Alignment.CenterHorizontally,
        ) {
            // ---- Header ----
            Row(Modifier.fillMaxWidth().padding(top = 8.dp), verticalAlignment = Alignment.CenterVertically) {
                Column(Modifier.weight(1f)) {
                    Text("Lia", fontSize = 34.sp, fontWeight = FontWeight.Bold,
                        color = MaterialTheme.colorScheme.primary)
                    Text(if (liaActive) "Sedang menemanimu ✨" else "Asisten mengambang di layarmu",
                        fontSize = 13.sp, color = MaterialTheme.colorScheme.onSurfaceVariant)
                }
                IconButton(onClick = { showSettings = !showSettings }) {
                    Icon(if (showSettings) Icons.Filled.Close else Icons.Filled.Settings,
                        contentDescription = "Setelan")
                }
            }

            Spacer(Modifier.height(24.dp))

            if (showSettings) {
                AiSettingsCard(prefs)
                Spacer(Modifier.height(16.dp))
            } else {
                // ---- Pilih karakter ----
                Text("Pilih penampilan", fontSize = 13.sp, fontWeight = FontWeight.Medium,
                    color = MaterialTheme.colorScheme.onSurfaceVariant,
                    modifier = Modifier.align(Alignment.Start))
                Spacer(Modifier.height(10.dp))
                Row(horizontalArrangement = Arrangement.spacedBy(14.dp), modifier = Modifier.fillMaxWidth()) {
                    CharacterCard("Kimono", "🤍", selectedChar == CharKimono, Modifier.weight(1f)) {
                        selectedChar = CharKimono
                        if (liaActive) UnityBridge.switchCharacter(CharKimono)
                    }
                    CharacterCard("Dress", "🖤", selectedChar == CharDress, Modifier.weight(1f)) {
                        selectedChar = CharDress
                        if (liaActive) UnityBridge.switchCharacter(CharDress)
                    }
                }

                Spacer(Modifier.height(28.dp))

                // ---- Tombol utama Munculkan / Sembunyikan ----
                Button(
                    onClick = {
                        if (!liaActive) { onStart(); liaActive = true }
                        else { onStop(); liaActive = false }
                    },
                    enabled = canStart,
                    modifier = Modifier.fillMaxWidth().height(64.dp),
                    shape = RoundedCornerShape(20.dp),
                    colors = ButtonDefaults.buttonColors(
                        containerColor = if (liaActive) MaterialTheme.colorScheme.errorContainer
                        else MaterialTheme.colorScheme.primary,
                        contentColor = if (liaActive) MaterialTheme.colorScheme.onErrorContainer
                        else MaterialTheme.colorScheme.onPrimary,
                    ),
                ) {
                    Text(if (liaActive) "Sembunyikan Lia" else "✦  Munculkan Lia",
                        fontSize = 18.sp, fontWeight = FontWeight.SemiBold)
                }

                Spacer(Modifier.height(12.dp))

                // ---- Ngobrol (muncul saat aktif) ----
                AnimatedVisibility(visible = liaActive) {
                    OutlinedButton(
                        onClick = onOpenChat,
                        modifier = Modifier.fillMaxWidth().height(56.dp),
                        shape = RoundedCornerShape(18.dp),
                    ) {
                        Text("💬  Ngobrol dengan Lia", fontSize = 16.sp)
                    }
                }

                // ---- Izin (hanya kalau kurang) ----
                if (!canStart) {
                    Spacer(Modifier.height(20.dp))
                    PermissionPrompt(
                        hasOverlay = hasOverlay,
                        hasBattery = hasBattery,
                        isMiui = isMiui,
                        onGrantOverlay = { overlayLauncher.launch(PermissionHelper.overlayPermissionIntent(context)) },
                        onGrantBattery = { batteryLauncher.launch(PermissionHelper.batteryWhitelistIntent(context)) },
                    )
                } else if (!hasBattery) {
                    Spacer(Modifier.height(14.dp))
                    Text("Tip: matikan pembatasan baterai biar Lia tidak ditutup sistem.",
                        fontSize = 11.sp, color = MaterialTheme.colorScheme.onSurfaceVariant)
                    TextButton(onClick = { batteryLauncher.launch(PermissionHelper.batteryWhitelistIntent(context)) }) {
                        Text("Atur baterai")
                    }
                }

                Spacer(Modifier.height(20.dp))
                Text(
                    if (liaActive) "Lia hidup di bawah layar — dia gerak & bereaksi sendiri. Buka app lain, dia tetap menemani."
                    else "Setelah muncul, Lia idle & jalan-jalan sendiri di tepi bawah layar. Ketuk Ngobrol untuk bicara.",
                    fontSize = 12.sp, color = MaterialTheme.colorScheme.onSurfaceVariant,
                    modifier = Modifier.padding(horizontal = 4.dp)
                )
            }
        }
    }
}

@Composable
private fun CharacterCard(
    name: String, emoji: String, selected: Boolean,
    modifier: Modifier = Modifier, onClick: () -> Unit,
) {
    val border = if (selected) MaterialTheme.colorScheme.primary else Color.Transparent
    val bg = if (selected) MaterialTheme.colorScheme.primaryContainer
    else MaterialTheme.colorScheme.surfaceVariant.copy(alpha = 0.5f)
    Column(
        modifier
            .clip(RoundedCornerShape(20.dp))
            .background(bg)
            .border(2.dp, border, RoundedCornerShape(20.dp))
            .clickable(onClick = onClick)
            .padding(vertical = 24.dp),
        horizontalAlignment = Alignment.CenterHorizontally,
    ) {
        Text(emoji, fontSize = 40.sp)
        Spacer(Modifier.height(8.dp))
        Text(name, fontSize = 15.sp,
            fontWeight = if (selected) FontWeight.Bold else FontWeight.Normal)
    }
}

@Composable
private fun PermissionPrompt(
    hasOverlay: Boolean, hasBattery: Boolean, isMiui: Boolean,
    onGrantOverlay: () -> Unit, onGrantBattery: () -> Unit,
) {
    Card(
        colors = CardDefaults.cardColors(containerColor = MaterialTheme.colorScheme.tertiaryContainer),
        shape = RoundedCornerShape(18.dp),
    ) {
        Column(Modifier.padding(18.dp), verticalArrangement = Arrangement.spacedBy(10.dp)) {
            Text("Butuh 1 izin dulu", fontSize = 15.sp, fontWeight = FontWeight.SemiBold)
            Text("Lia perlu izin \"tampil di atas aplikasi lain\" supaya bisa mengambang di layar.",
                fontSize = 12.sp)
            if (!hasOverlay) {
                Button(onClick = onGrantOverlay, modifier = Modifier.fillMaxWidth()) {
                    Text("Beri izin tampil di layar")
                }
            }
        }
    }
}

@Composable
private fun AiSettingsCard(prefs: AiPrefs) {
    var geminiKey by remember { mutableStateOf(prefs.geminiApiKey) }
    var elevenKey by remember { mutableStateOf(prefs.elevenLabsApiKey) }
    var voiceId by remember { mutableStateOf(prefs.voiceId) }
    var ttsOn by remember { mutableStateOf(prefs.ttsEnabled) }
    var saved by remember { mutableStateOf(false) }

    Card(shape = RoundedCornerShape(18.dp)) {
        Column(Modifier.padding(18.dp), verticalArrangement = Arrangement.spacedBy(10.dp)) {
            Text("Setelan AI", fontSize = 18.sp, fontWeight = FontWeight.Bold)
            Text("Otak Lia (Gemini) & suara (ElevenLabs). Sudah terisi dari konfigurasi — ubah kalau perlu.",
                fontSize = 11.sp, color = MaterialTheme.colorScheme.onSurfaceVariant)

            OutlinedTextField(geminiKey, { geminiKey = it; saved = false },
                Modifier.fillMaxWidth(), label = { Text("Gemini API key") }, singleLine = true)
            OutlinedTextField(elevenKey, { elevenKey = it; saved = false },
                Modifier.fillMaxWidth(), label = { Text("ElevenLabs API key (opsional)") }, singleLine = true)
            OutlinedTextField(voiceId, { voiceId = it; saved = false },
                Modifier.fillMaxWidth(), label = { Text("Voice ID") }, singleLine = true)
            Row(verticalAlignment = Alignment.CenterVertically) {
                Switch(ttsOn, { ttsOn = it; saved = false })
                Spacer(Modifier.width(10.dp))
                Text("Aktifkan suara (TTS)", fontSize = 14.sp)
            }
            Button(
                onClick = {
                    prefs.geminiApiKey = geminiKey
                    prefs.elevenLabsApiKey = elevenKey
                    prefs.voiceId = voiceId
                    prefs.ttsEnabled = ttsOn
                    saved = true
                },
                modifier = Modifier.fillMaxWidth(),
            ) { Text(if (saved) "✓ Tersimpan" else "Simpan") }
        }
    }
}

@Composable
private fun isSystemDark(): Boolean =
    androidx.compose.foundation.isSystemInDarkTheme()
