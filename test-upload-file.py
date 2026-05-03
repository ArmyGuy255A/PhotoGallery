#!/usr/bin/env python3
"""Test file upload with actual file"""

from playwright.sync_api import sync_playwright
import time
import os

def test_photo_upload_with_file():
    with sync_playwright() as p:
        browser = p.chromium.launch(headless=True)
        page = browser.new_page()
        
        print("🎬 Testing photo upload workflow...")
        
        try:
            # Navigate to app
            print("\n1️⃣  Navigating to dashboard...")
            page.goto('http://localhost:4200/dashboard')
            page.wait_for_load_state('networkidle')
            time.sleep(1)
            
            # Find and open first album
            album_cards = page.locator('.album-card')
            if album_cards.count() > 0:
                print(f"2️⃣  Found {album_cards.count()} albums")
                view_btn = album_cards.first.locator('text="View Album"')
                if view_btn.is_visible():
                    view_btn.click()
                else:
                    album_cards.first.click()
                
                page.wait_for_load_state('networkidle')
                time.sleep(2)
                
                print("3️⃣  Looking for upload components...")
                
                # Try multiple selectors for upload zone
                selectors = [
                    '.upload-zone',
                    '.upload-card',
                    'app-photo-upload',
                    'input[type="file"]'
                ]
                
                found = False
                file_input = None
                
                for selector in selectors:
                    elements = page.locator(selector)
                    count = elements.count()
                    print(f"    - {selector}: {count} elements")
                    
                    if selector == 'input[type="file"]' and count > 0:
                        file_input = elements.first
                        found = True
                
                if file_input and found:
                    print("4️⃣  File input found! Testing upload...")
                    
                    # Find a sample photo
                    sample_photo_dir = r"D:\repos\PhotoGallery\PhotoGallery\SamplePhotos"
                    if os.path.exists(sample_photo_dir):
                        photos = [f for f in os.listdir(sample_photo_dir) 
                                 if f.lower().endswith(('.jpg', '.jpeg', '.png', '.cr2', '.raw'))]
                        
                        if photos:
                            photo_file = os.path.join(sample_photo_dir, photos[0])
                            print(f"    📁 Selected: {photos[0]} ({os.path.getsize(photo_file) / 1024:.1f} KB)")
                            
                            # Upload file
                            file_input.set_input_files(photo_file)
                            print("5️⃣  File selected, waiting for upload...")
                            
                            time.sleep(3)
                            page.wait_for_load_state('networkidle')
                            
                            # Check for progress or success
                            page.screenshot(path='/tmp/after-upload.png')
                            
                            # Check for status indicators
                            progress = page.locator('.progress, .badge')
                            print(f"6️⃣  Progress/status elements: {progress.count()}")
                            
                            # Check if photos loaded
                            photos_section = page.locator('.photos-grid')
                            if photos_section.count() > 0:
                                print(f"✅ Photos section found!")
                                photo_cards = page.locator('.photo-card')
                                print(f"✅ Photo cards: {photo_cards.count()}")
                            
                            print("\n✅ Upload test completed!")
                            return True
                        else:
                            print("❌ No sample photos found")
                    else:
                        print(f"❌ Sample photos directory not found: {sample_photo_dir}")
                else:
                    print("❌ File input not found")
                    page.screenshot(path='/tmp/no-file-input.png')
            else:
                print("❌ No albums found")
            
            browser.close()
            return False
            
        except Exception as e:
            print(f"❌ Error: {e}")
            page.screenshot(path='/tmp/error.png')
            browser.close()
            return False

if __name__ == '__main__':
    test_photo_upload_with_file()
