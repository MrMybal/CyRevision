"""Render app-owned vector diagrams and PNG companions; requires Pillow for PNG."""
import html
import json
import shutil
import textwrap
from pathlib import Path
from PIL import Image, ImageDraw, ImageFont

ROOT = Path(__file__).resolve().parents[1]
OUT = ROOT / 'documentation/assets'
OUT.mkdir(parents=True, exist_ok=True)


def font(size, bold=False):
    for name in (('arialbd.ttf' if bold else 'arial.ttf'), ('DejaVuSans-Bold.ttf' if bold else 'DejaVuSans.ttf')):
        try: return ImageFont.truetype(name, size)
        except OSError: pass
    return ImageFont.load_default(size=size)


for identifier, en_title, fr_title, en_nodes, fr_nodes, en_note, fr_note in json.loads((ROOT / 'scripts/documentation-diagrams.json').read_text(encoding='utf-8')):
    for language, title, nodes, note in (('en', en_title, en_nodes, en_note), ('fr', fr_title, fr_nodes, fr_note)):
        image = Image.new('RGB', (1280, 760), '#111820')
        draw = ImageDraw.Draw(image)
        svg = ['<svg xmlns="http://www.w3.org/2000/svg" width="1280" height="760" viewBox="0 0 1280 760">', '<rect width="1280" height="760" fill="#111820"/>']
        def label(x, y, text, size, color, bold=False):
            draw.text((x,y), text, font=font(size,bold), fill=color)
            svg.append(f'<text x="{x}" y="{y+size}" fill="{color}" font-family="sans-serif" font-size="{size}" font-weight="{700 if bold else 400}">{html.escape(text)}</text>')
        label(48, 30, 'CYREVISION / ' + ('USER GUIDE' if language == 'en' else 'GUIDE UTILISATEUR'), 18, '#F29A43', True)
        for index, line in enumerate(textwrap.wrap(title, 49)):
            label(48, 76 + index*44, line, 34, '#F2F5F7', True)
        for index, node in enumerate(nodes):
            heading, body = node.split('|', 1)
            x = 48 + (index%2)*616; y = 220 + (index//2)*170
            draw.rounded_rectangle((x,y,x+568,y+140), radius=14, fill='#1F2B36', outline='#415467', width=2)
            svg.append(f'<rect x="{x}" y="{y}" width="568" height="140" rx="14" fill="#1F2B36" stroke="#415467" stroke-width="2"/>')
            label(x+22,y+14,f'{index+1:02}',19,'#F29A43',True)
            label(x+72,y+14,heading,25,'#FFFFFF',True)
            for line_index,line in enumerate(textwrap.wrap(body,39)):
                label(x+22,y+65+line_index*28,line,23,'#CAD8E2')
        for index,line in enumerate(textwrap.wrap(note,88)):
            label(48,610+index*30,line,23,'#AAC8C5')
        label(48,708,'EN / FR · ' + ('Concept diagram — not a live status' if language=='en' else 'Schéma conceptuel — pas un état en direct'),16,'#91A3B1')
        svg.append('</svg>')
        (OUT/f'{identifier}.{language}.svg').write_text('\n'.join(svg)+'\n',encoding='utf-8')
        image.save(OUT/f'{identifier}.{language}.png', optimize=True)

for name in ('workspace-v2.png','history-v2.png','split-diff-v2.png'):
    shutil.copyfile(ROOT/'docs/images'/name, OUT/name)
print('Rendered 26 localized diagrams and copied 3 real application screenshots.')
