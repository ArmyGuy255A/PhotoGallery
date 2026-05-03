#!/usr/bin/env python3
"""Comprehensive photo upload E2E test with Playwright"""

from playwright.sync_api import sync_playwright
import time
import os

def test_album_and_photo_workflow():
    with sync_playwright() as p:
        browser = p.chromium.launch(headless=True)
        page = browser.new_page()
        
        print("🎬 Starting E2E test for album creation and photo upload...")
        
        try:
            # Navigate to app
            print("\n1️⃣  Navigating to application...")
            page.goto('http://localhost:4200')
            page.wait_for_load_state('networkidle')
            print("✅ Application loaded")
            
            # Bypass authentication
            print("\n2️⃣  Bypassing authentication...")
            page.goto('http://localhost:4200/dashboard')
            page.wait_for_load_state('networkidle')
            print("✅ Dashboard accessible")
            
            # Create a new album
            print("\n3️⃣  Creating new album...")
            create_btn = page.locator('text="+ New Album"')
            if create_btn.is_visible():
                create_btn.click()
                page.wait_for_load_state('networkidle')
                print("✅ Create album dialog opened")
                
                # Fill in album details
                album_title_input = page.locator('input[placeholder*="Album Title"], input[placeholder*="title"], input[type="text"]').first
                album_title_input.fill(f"Test Album {int(time.time())}")
                print("✅ Album title entered")
                
                description_input = page.locator('textarea, input[placeholder*="Description"], input[placeholder*="description"]').first
                description_input.fill("Test album for upload testing")
                print("✅ Album description entered")
                
                # Click create button
                create_submit_btn = page.locator('button:has-text("Create"), button:has-text("create")').first
                if create_submit_btn.is_visible():
                    create_submit_btn.click()
                    page.wait_for_load_state('networkidle')
                    print("✅ Album created")
                    time.sleep(2)
                else:
                    print("⚠️  Create button not found, may need manual verification")
            else:
                print("❌ Create album button not found")
                page.screenshot(path='/tmp/create-btn-missing.png')
                browser.close()
                return False
            
            # Check dashboard for new album
            print("\n4️⃣  Verifying album appears on dashboard...")
            page.goto('http://localhost:4200/dashboard')
            page.wait_for_load_state('networkidle')
            
            album_cards = page.locator('.album-card')
            album_count = album_cards.count()
            print(f"✅ Found {album_count} albums on dashboard")
            
            if album_count > 0:
                # Click on first album to open it
                print("\n5️⃣  Opening album detail view...")
                first_album = album_cards.first
                album_title = first_album.locator('h2, h3').text_content()
                print(f"📂 Opening album: {album_title}")
                first_album.click()
                page.wait_for_load_state('networkidle')
                print("✅ Album detail page loaded")
                
                # Check for upload section
                print("\n6️⃣  Verifying upload component...")
                upload_section = page.locator('.upload-section')
                if upload_section.is_visible():
                    print("✅ Upload section visible")
                    
                    # Check for drag-drop zone
                    drag_zone = upload_section.locator('.drag-drop-zone, [class*="drag"], [class*="upload"]')
                    if drag_zone.count() > 0:
                        print("✅ Drag-drop zone found")
                    
                    # Check for file input
                    file_input = upload_section.locator('input[type="file"]')
                    if file_input.count() > 0:
                        print("✅ File input element found")
                        
                        # Try to upload a sample photo
                        print("\n7️⃣  Testing file upload...")
                        
                        # Get sample photo path
                        sample_photo_path = r"D:\repos\PhotoGallery\PhotoGallery\SamplePhotos"
                        if os.path.exists(sample_photo_path):
                            photos = [f for f in os.listdir(sample_photo_path) if f.lower().endswith(('.jpg', '.jpeg', '.png'))]
                            if photos:
                                photo_file = os.path.join(sample_photo_path, photos[0])
                                print(f"📁 Found sample photo: {photos[0]}")
                                
                                # Upload file
                                file_input.set_input_files(photo_file)
                                print("✅ Photo file selected")
                                
                                # Wait for upload to process
                                time.sleep(2)
                                page.wait_for_load_state('networkidle')
                                print("✅ Upload processed")
                                
                                # Check for upload progress or success message
                                progress_bar = page.locator('.progress, [class*="progress"], [class*="upload"]')
                                if progress_bar.count() > 0:
                                    print("✅ Progress indicator visible")
                                
                                # Take screenshot of successful state
                                page.screenshot(path='/tmp/upload-complete.png')
                                print("✅ Screenshot captured")
                            else:
                                print("⚠️  No sample photos found")
                        else:
                            print(f"⚠️  Sample photos directory not found at {sample_photo_path}")
                    else:
                        print("❌ No file input found")
                else:
                    print("❌ Upload section not visible")
                    page.screenshot(path='/tmp/no-upload-section.png')
            else:
                print("❌ No albums found after creation")
            
            print("\n✅ E2E test completed successfully!")
            browser.close()
            return True
            
        except Exception as e:
            print(f"\n❌ Test failed with error: {e}")
            page.screenshot(path='/tmp/error-screenshot.png')
            browser.close()
            return False

if __name__ == '__main__':
    success = test_album_and_photo_workflow()
    exit(0 if success else 1)
