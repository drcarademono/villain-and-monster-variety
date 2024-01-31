import glob
import os
import xml.etree.ElementTree as ET

# Pattern to match PNG files ending with -0.png in the current folder
png_files_pattern = './*-0.png'

def create_xml_for_png(png_file):
    # Create the XML structure
    root = ET.Element('info')
    scaleX = ET.SubElement(root, 'scaleX')
    scaleY = ET.SubElement(root, 'scaleY')
    scaleX.text = '0.666'
    scaleY.text = '0.666'
    
    # Convert the PNG filename to an XML filename
    xml_filename = png_file.rsplit('.', 1)[0] + '.xml'
    
    # Create and write the XML file
    tree = ET.ElementTree(root)
    tree.write(xml_filename)
    print(f"Created {xml_filename}")

# Iterate over all PNG files in the current folder that match the pattern and create XMLs
for png_file in glob.glob(png_files_pattern):
    create_xml_for_png(png_file)

