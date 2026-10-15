param([string]$Out = (Join-Path $PSScriptRoot '..\src\GhNotify.Platform.Windows\Sounds'))
$ErrorActionPreference = 'Stop'
New-Item -ItemType Directory -Force $Out | Out-Null
$rate = 44100

function Write-Wav([string]$path, [double[]]$samples) {
    $fs = [IO.File]::Create($path); $w = New-Object IO.BinaryWriter($fs)
    $data = $samples.Length * 2
    $w.Write([Text.Encoding]::ASCII.GetBytes('RIFF')); $w.Write([int](36 + $data)); $w.Write([Text.Encoding]::ASCII.GetBytes('WAVEfmt '))
    $w.Write([int]16); $w.Write([int16]1); $w.Write([int16]1); $w.Write([int]$rate); $w.Write([int]($rate * 2)); $w.Write([int16]2); $w.Write([int16]16)
    $w.Write([Text.Encoding]::ASCII.GetBytes('data')); $w.Write([int]$data)
    foreach ($s in $samples) { $w.Write([int16][Math]::Max(-32767, [Math]::Min(32767, [Math]::Round($s * 32767)))) }
    $w.Dispose()
}

# notes: array of @(freqHz, seconds, volume); each note gets a fast attack and exponential decay
function Tone($notes) {
    $out = New-Object System.Collections.Generic.List[double]
    foreach ($n in $notes) {
        $f, $sec, $vol = $n; $count = [int]($rate * $sec)
        for ($i = 0; $i -lt $count; $i++) {
            $t = $i / $rate
            $env = [Math]::Min(1, $t / 0.005) * [Math]::Exp(-4 * $t / $sec)
            $out.Add($vol * $env * ([Math]::Sin(2 * [Math]::PI * $f * $t) + 0.3 * [Math]::Sin(4 * [Math]::PI * $f * $t)) / 1.3)
        }
    }
    , $out.ToArray()
}

Write-Wav (Join-Path $Out 'chime.wav') (Tone @(@(880, 0.18, 0.6), @(1320, 0.35, 0.6)))
Write-Wav (Join-Path $Out 'ping.wav')  (Tone @(,@(1760, 0.2, 0.5)))
Write-Wav (Join-Path $Out 'alert.wav') (Tone @(@(1000, 0.12, 0.6), @(0, 0.06, 0), @(1000, 0.12, 0.6), @(0, 0.06, 0), @(1000, 0.12, 0.6)))
Write-Wav (Join-Path $Out 'alarm.wav') (Tone @(@(900, 0.15, 0.7), @(600, 0.15, 0.7), @(900, 0.15, 0.7), @(600, 0.15, 0.7), @(900, 0.15, 0.7), @(600, 0.3, 0.7)))
Write-Wav (Join-Path $Out 'soft.wav')  (Tone @(,@(440, 0.45, 0.35)))
Write-Host "wrote 5 sounds to $Out"
