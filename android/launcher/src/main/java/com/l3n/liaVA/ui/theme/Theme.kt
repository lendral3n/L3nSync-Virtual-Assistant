package com.l3n.liaVA.ui.theme

import androidx.compose.foundation.isSystemInDarkTheme
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.darkColorScheme
import androidx.compose.material3.lightColorScheme
import androidx.compose.runtime.Composable

private val DarkColors = darkColorScheme(
    primary = LiaPrimary,
    onPrimary = LiaOnSurface,
    primaryContainer = LiaPrimaryDark,
    secondary = LiaSecondary,
    tertiary = LiaTertiary,
    background = LiaBackground,
    surface = LiaSurface,
    surfaceVariant = LiaSurfaceVariant,
    onSurface = LiaOnSurface,
    onSurfaceVariant = LiaOnSurfaceMuted
)

private val LightColors = lightColorScheme(
    primary = LiaPrimaryDark,
    secondary = LiaPrimary,
    tertiary = LiaSecondary
)

@Composable
fun LiaVATheme(
    darkTheme: Boolean = isSystemInDarkTheme(),
    content: @Composable () -> Unit
) {
    val colors = if (darkTheme) DarkColors else LightColors
    MaterialTheme(
        colorScheme = colors,
        content = content
    )
}
