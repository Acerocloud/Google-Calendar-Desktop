from pathlib import Path
from PIL import Image, ImageDraw
im=Image.new('RGBA',(256,256),(0,0,0,0))
d=ImageDraw.Draw(im)
d.rounded_rectangle((12,12,244,244),radius=46,fill='#202426')
d.rounded_rectangle((51,58,205,207),radius=15,outline='#ECEDE9',width=10)
d.line((54,100,202,100),fill='#ECEDE9',width=8)
for x in (91,165):d.rounded_rectangle((x-5,41,x+5,77),radius=5,fill='#ECEDE9')
for y in (126,157,184):
 for x in (80,112,144,176):d.ellipse((x-5,y-5,x+5,y+5),fill='#ECEDE9')
d.rounded_rectangle((131,144,157,170),radius=6,fill='#9FBAA9')
im.save(Path(__file__).with_name('app.ico'),sizes=[(16,16),(32,32),(48,48),(64,64),(128,128),(256,256)])
