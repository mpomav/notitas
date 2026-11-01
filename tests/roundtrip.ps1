# Checks the Markdown format against the compiled exe. Run with Windows PowerShell 5.1:
#   powershell -ExecutionPolicy Bypass -File tests\roundtrip.ps1
$exe = Join-Path $PSScriptRoot '..\dist\Notitas.exe'
$asm = [Reflection.Assembly]::LoadFile((Resolve-Path $exe).Path)
$file = $asm.GetType('Notitas.NoteFile')
$noteType = $asm.GetType('Notitas.Note')
$script:failed = 0

function Check($name, $actual, $expected) {
    if ("$actual" -ceq "$expected") { "ok   $name" }
    else { "FAIL $name`n     got:  $actual`n     want: $expected"; $script:failed++ }
}
function Parse($text) { $file.GetMethod('Parse').Invoke($null, @($text, $null)) }
function Serialize($note) { $file.GetMethod('Serialize').Invoke($null, @($note)) }
function BaseName($note) { $file.GetMethod('BaseName').Invoke($null, @($note)) }

$own = "---`nid: abc`ntitle: `"Dijo \`"hola\`" C:\\x`"`ntags:`n  - trabajo`n  - idea/nueva`ncolor: mint`ncreated: 2026-01-02T03:04:05`nupdated: 2026-01-03T03:04:05`n---`nLínea uno`n`n---`nMás texto ñ`n"
$n = Parse $own
Check 'title with quotes and backslash' $n.Title 'Dijo "hola" C:\x'
Check 'tags as list' ($n.Tags -join ',') 'trabajo,idea/nueva'
Check 'color' $n.Color 'mint'
Check 'body keeps a --- inside' $n.Body "Línea uno`n`n---`nMás texto ñ`n"
Check 'own file round-trips byte for byte' (Serialize $n) $own

$obsidian = "---`naliases:`n  - uno`n  - dos`ncssclasses: mi-clase`ntags: [a, B]`n---`nTexto"
$n = Parse $obsidian
Check 'inline tags, lower-cased' ($n.Tags -join ',') 'a,b'
$out = Serialize $n
Check 'foreign multi-line property kept' ($out -match "aliases:`n  - uno`n  - dos") 'True'
Check 'foreign single property kept' ($out -match 'cssclasses: mi-clase') 'True'

$n = Parse 'Solo texto sin frontmatter'
Check 'plain file body' $n.Body 'Solo texto sin frontmatter'
$n = Parse "---`nsin cerrar`nhola"
Check 'unclosed frontmatter stays in body' $n.Body "---`nsin cerrar`nhola"
$n = Parse "$([char]0xFEFF)---`r`ntitle: `"BOM`"`r`n---`r`ncrlf"
Check 'BOM and CRLF' "$($n.Title)|$($n.Body)" 'BOM|crlf'
$n = Parse ""
Check 'empty file' $n.Body ''

$norm = $noteType.GetMethod('NormalizeTag')
Check 'tag normalize' $norm.Invoke($null, @(' #Mi Tag! ')) 'mi-tag'
$ene = [char]0xF1
Check 'tag keeps accents and nesting' $norm.Invoke($null, @("A${ene}os/${ene}u")) "a${ene}os/${ene}u"
Check 'tag only symbols' $norm.Invoke($null, @('#!!')) ''

$n = [Activator]::CreateInstance($noteType)
$n.Title = 'a/b:c*?.'
Check 'file name drops invalid chars' (BaseName $n) 'a b c'
$n.Title = 'CON'
Check 'reserved device name' (BaseName $n) 'Nota CON'
$n.Title = '..'
Check 'dots only falls back' ((BaseName $n) -like 'Nota 20*') 'True'

if ($script:failed) { "$script:failed failed"; exit 1 } else { 'all passed' }
