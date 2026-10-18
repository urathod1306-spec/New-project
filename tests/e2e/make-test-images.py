"""Creates the test images/files used by the browser tests (run: python3 -I make-test-images.py <folder>)."""
import os, sys
from PIL import Image, ImageDraw
out = sys.argv[1] if len(sys.argv) > 1 else 'img'
os.makedirs(out, exist_ok=True)
im = Image.new('RGB', (600, 400), 'white'); d = ImageDraw.Draw(im)
d.rectangle([0, 0, 299, 399], fill=(220, 30, 30)); d.rectangle([300, 0, 599, 399], fill=(30, 30, 220))
exif = im.getexif(); exif[0x0112] = 6; exif[0x010F] = 'TestCam'
exif[0x8825] = {1: 'N', 2: (19.0, 4.0, 30.0), 3: 'E', 4: (72.0, 52.0, 10.0)}
im.save(os.path.join(out, 'rotated_gps.jpg'), exif=exif.tobytes(), quality=90)
Image.frombytes('RGB', (1400, 1400), os.urandom(1400 * 1400 * 3)).save(os.path.join(out, 'over2mb.png'))
open(os.path.join(out, 'evil.jpg'), 'wb').write(b'MZ\x90\x00' + os.urandom(5000))
open(os.path.join(out, 'fake.png'), 'wb').write(b'GIF89a' + os.urandom(2000))
open(os.path.join(out, 'proposal.pdf'), 'wb').write(b'%PDF-1.4\n1 0 obj<<>>endobj\ntrailer<<>>\n%%EOF\n')
open(os.path.join(out, 'notreal.pdf'), 'wb').write(b'MZ' + os.urandom(3000))
import zipfile
with zipfile.ZipFile(os.path.join(out, 'scope.docx'), 'w') as z:   # minimal Word-like package (ZIP signature)
    z.writestr('[Content_Types].xml', '<?xml version="1.0"?><Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types"/>')
    z.writestr('word/document.xml', '<w:document xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main"/>')
print('test files written to', out)
