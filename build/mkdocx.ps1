# 从结构化的 manual.txt 生成 .docx (纯 OOXML + zip, 不依赖任何外部库)
Add-Type -AssemblyName System.IO.Compression | Out-Null
Add-Type -AssemblyName System.IO.Compression.FileSystem | Out-Null

$src = $args[0]
$lines = [System.IO.File]::ReadAllLines($src, (New-Object System.Text.UTF8Encoding($false)))

function Esc([string]$s) {
    $s = $s -replace '&', '&amp;'
    $s = $s -replace '<', '&lt;'
    $s = $s -replace '>', '&gt;'
    return $s
}

$sb = New-Object System.Text.StringBuilder
[void]$sb.Append('<?xml version="1.0" encoding="UTF-8" standalone="yes"?><w:document xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main"><w:body>')

$outPath = ''
$nH1 = 0; $nH2 = 0; $nH3 = 0; $nT = 0; $nP = 0

function W([string]$s) { [void]$script:sb.Append($s) }

function Run([string]$t, [string]$extra) {
    W ('<w:r><w:rPr>' + $extra + '</w:rPr><w:t xml:space="preserve">' + (Esc $t) + '</w:t></w:r>')
}

function Para([string]$style, [string]$text) {
    $script:nP++
    # 注意: 这里必须用括号包住拼接, 否则 PowerShell 会把 '+' 当成独立参数
    W ('<w:p><w:pPr><w:pStyle w:val="' + $style + '"/></w:pPr>')
    Run $text ''
    W '</w:p>'
}

function Bullet([string]$text) {
    $script:nP++
    W '<w:p><w:pPr><w:pStyle w:val="Normal"/><w:spacing w:after="60"/><w:ind w:left="420" w:hanging="220"/></w:pPr>'
    Run (([char]0x2022) + ' ' + $text) ''
    W '</w:p>'
}

function Note([string]$text) {
    $script:nP++
    W '<w:p><w:pPr><w:pStyle w:val="Normal"/><w:spacing w:before="80" w:after="160"/><w:ind w:left="200"/><w:pBdr><w:left w:val="single" w:sz="18" w:space="8" w:color="C55A11"/></w:pBdr></w:pPr>'
    Run $text '<w:i/><w:color w:val="7F6000"/>'
    W '</w:p>'
}

function Table([string]$spec) {
    $script:nT++
    $rows = $spec -split '\|\|'
    $ncol = ($rows[0] -split ';').Count
    W '<w:tbl><w:tblPr><w:tblW w:w="5000" w:type="pct"/><w:tblBorders>'
    foreach ($b in @('top','left','bottom','right','insideH','insideV')) {
        W ('<w:' + $b + ' w:val="single" w:sz="4" w:space="0" w:color="A6A6A6"/>')
    }
    W '</w:tblBorders></w:tblPr><w:tblGrid>'
    for ($i = 0; $i -lt $ncol; $i++) { W ('<w:gridCol w:w="' + [int](9000 / $ncol) + '"/>') }
    W '</w:tblGrid>'
    $r = 0
    foreach ($row in $rows) {
        $cells = $row -split ';'
        W '<w:tr>'
        if ($r -eq 0) { W '<w:trPr><w:tblHeader/></w:trPr>' }
        for ($i = 0; $i -lt $ncol; $i++) {
            $txt = ''
            if ($i -lt $cells.Count) { $txt = $cells[$i] }
            W '<w:tc><w:tcPr>'
            if ($r -eq 0) { W '<w:shd w:val="clear" w:color="auto" w:fill="EFEFEF"/>' }
            W '<w:vAlign w:val="center"/></w:tcPr>'
            W '<w:p><w:pPr><w:spacing w:before="40" w:after="40"/></w:pPr>'
            if ($r -eq 0) { Run $txt '<w:b/><w:sz w:val="20"/>' } else { Run $txt '<w:sz w:val="20"/>' }
            W '</w:p></w:tc>'
        }
        W '</w:tr>'
        $r++
    }
    W '</w:tbl>'
    W '<w:p><w:pPr><w:spacing w:after="140"/></w:pPr></w:p>'
}

foreach ($ln in $lines) {
    if ($ln.Trim().Length -eq 0) { continue }
    $i = $ln.IndexOf('|')
    if ($i -lt 0) { continue }
    $tag = $ln.Substring(0, $i)
    $val = $ln.Substring($i + 1)
    switch ($tag) {
        'OUT' { $outPath = $val }
        'TITLE' {
            W '<w:p><w:pPr><w:jc w:val="center"/><w:spacing w:before="200" w:after="100"/></w:pPr>'
            Run $val '<w:b/><w:sz w:val="52"/><w:color w:val="16A34A"/>'
            W '</w:p>'
        }
        'SUB' {
            W '<w:p><w:pPr><w:jc w:val="center"/><w:spacing w:after="40"/></w:pPr>'
            Run $val '<w:sz w:val="24"/><w:color w:val="595959"/>'
            W '</w:p>'
        }
        'META' {
            W '<w:p><w:pPr><w:jc w:val="center"/><w:spacing w:after="360"/><w:pBdr><w:bottom w:val="single" w:sz="6" w:space="8" w:color="D9D9D9"/></w:pBdr></w:pPr>'
            Run $val '<w:sz w:val="18"/><w:color w:val="808080"/>'
            W '</w:p>'
        }
        'H1' { $nH1++; Para 'Heading1' $val }
        'H2' { $nH2++; Para 'Heading2' $val }
        'H3' { $nH3++; Para 'Heading3' $val }
        'P'  { Para 'Normal' $val }
        'B'  { Bullet $val }
        'N'  { Note $val }
        'C'  { Para 'Code' $val }
        'T'  { Table $val }
    }
}

W '<w:sectPr><w:pgSz w:w="11906" w:h="16838"/><w:pgMar w:top="1440" w:right="1276" w:bottom="1440" w:left="1276" w:header="851" w:footer="992" w:gutter="0"/></w:sectPr>'
W '</w:body></w:document>'
$docXml = $sb.ToString()

# 写文件前先校验 XML, 不合法就直接退出, 避免产出 Word 打不开的坏文件
try {
    $chk = New-Object System.Xml.XmlDocument
    $chk.LoadXml($docXml)
    '  XML 校验通过, 根节点=' + $chk.DocumentElement.Name
} catch {
    '  XML 校验失败: ' + $_.Exception.Message
    exit 1
}
'  段落 ' + $nP + ' 个, 表格 ' + $nT + ' 个'

$ct = '<?xml version="1.0" encoding="UTF-8" standalone="yes"?><Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types"><Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/><Default Extension="xml" ContentType="application/xml"/><Override PartName="/word/document.xml" ContentType="application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml"/><Override PartName="/word/styles.xml" ContentType="application/vnd.openxmlformats-officedocument.wordprocessingml.styles+xml"/></Types>'

$rels = '<?xml version="1.0" encoding="UTF-8" standalone="yes"?><Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships"><Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument" Target="word/document.xml"/></Relationships>'

$drels = '<?xml version="1.0" encoding="UTF-8" standalone="yes"?><Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships"><Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/styles" Target="styles.xml"/></Relationships>'

$font = 'Microsoft YaHei'
$styles = '<?xml version="1.0" encoding="UTF-8" standalone="yes"?><w:styles xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main">'
$styles += '<w:docDefaults><w:rPrDefault><w:rPr><w:rFonts w:ascii="' + $font + '" w:hAnsi="' + $font + '" w:eastAsia="' + $font + '" w:cs="' + $font + '"/><w:sz w:val="24"/><w:szCs w:val="24"/><w:lang w:val="en-US" w:eastAsia="zh-CN" w:bidi="ar-SA"/></w:rPr></w:rPrDefault><w:pPrDefault><w:pPr><w:spacing w:after="140" w:line="312" w:lineRule="auto"/></w:pPr></w:pPrDefault></w:docDefaults>'
$styles += '<w:style w:type="paragraph" w:default="1" w:styleId="Normal"><w:name w:val="Normal"/><w:qFormat/></w:style>'
$styles += '<w:style w:type="paragraph" w:styleId="Heading1"><w:name w:val="heading 1"/><w:basedOn w:val="Normal"/><w:next w:val="Normal"/><w:qFormat/><w:pPr><w:keepNext/><w:spacing w:before="400" w:after="160"/><w:outlineLvl w:val="0"/></w:pPr><w:rPr><w:rFonts w:ascii="' + $font + '" w:hAnsi="' + $font + '" w:eastAsia="' + $font + '"/><w:b/><w:sz w:val="36"/><w:szCs w:val="36"/><w:color w:val="1F3864"/></w:rPr></w:style>'
$styles += '<w:style w:type="paragraph" w:styleId="Heading2"><w:name w:val="heading 2"/><w:basedOn w:val="Normal"/><w:next w:val="Normal"/><w:qFormat/><w:pPr><w:keepNext/><w:spacing w:before="280" w:after="120"/><w:outlineLvl w:val="1"/></w:pPr><w:rPr><w:rFonts w:ascii="' + $font + '" w:hAnsi="' + $font + '" w:eastAsia="' + $font + '"/><w:b/><w:sz w:val="28"/><w:szCs w:val="28"/><w:color w:val="2E5496"/></w:rPr></w:style>'
$styles += '<w:style w:type="paragraph" w:styleId="Heading3"><w:name w:val="heading 3"/><w:basedOn w:val="Normal"/><w:next w:val="Normal"/><w:qFormat/><w:pPr><w:keepNext/><w:spacing w:before="240" w:after="100"/><w:outlineLvl w:val="2"/></w:pPr><w:rPr><w:rFonts w:ascii="' + $font + '" w:hAnsi="' + $font + '" w:eastAsia="' + $font + '"/><w:b/><w:sz w:val="24"/><w:szCs w:val="24"/><w:color w:val="333333"/></w:rPr></w:style>'
$styles += '<w:style w:type="paragraph" w:styleId="Code"><w:name w:val="Code"/><w:basedOn w:val="Normal"/><w:rPr><w:rFonts w:ascii="Consolas" w:hAnsi="Consolas" w:eastAsia="' + $font + '"/><w:sz w:val="20"/><w:color w:val="C7254E"/></w:rPr></w:style>'
$styles += '</w:styles>'

$tmp = $outPath + '.tmp'
if (Test-Path $tmp) { Remove-Item $tmp -Force }
$zip = [System.IO.Compression.ZipFile]::Open($tmp, [System.IO.Compression.ZipArchiveMode]::Create)
$enc = New-Object System.Text.UTF8Encoding($false)
foreach ($pair in @(@('[Content_Types].xml', $ct), @('_rels/.rels', $rels), @('word/_rels/document.xml.rels', $drels), @('word/styles.xml', $styles), @('word/document.xml', $docXml))) {
    $e = $zip.CreateEntry($pair[0])
    $s = $e.Open()
    $bytes = $enc.GetBytes($pair[1])
    $s.Write($bytes, 0, $bytes.Length)
    $s.Dispose()
}
$zip.Dispose()
if (Test-Path $outPath) { Remove-Item $outPath -Force }
Move-Item $tmp $outPath

'  输出 : ' + $outPath
'  大小 : ' + [math]::Round((Get-Item $outPath).Length / 1KB, 1) + ' KB'
'  一级标题 ' + $nH1 + ' / 二级 ' + $nH2 + ' / 三级 ' + $nH3 + ' / 表格 ' + $nT + ' / 段落 ' + $nP
