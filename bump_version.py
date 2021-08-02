import os
import glob
import re

files = glob.glob("*/manifest.json")

for f in files:
    name = os.path.basename(os.path.dirname(f))
    print("Bumping", name)
    with open(f) as file:
        text = file.read()

    # Bump mod version
    text = re.sub('"Version": "[\d.]+",', '"Version": "5.0",', text)

    # Bump api version
    text = re.sub('"MinimumApiVersion": "[\d.]+",', '"MinimumApiVersion": "3.12.0",', text)

    with open(f, "w") as file:
        file.write(text)
        
