#!/usr/bin/env python3
"""Detailed inspection of album detail page"""

from playwright.sync_api import sync_playwright
import time

def inspect_album_detail():
    with sync_playwright() as p:
        browser = p.chromium.launch(headless=True)
        page = browser.new_page()
        
        print("🔗 Navigating to dashboard...")
        page.goto('http://localhost:4200/dashboard')
        page.wait_for_load_state('networkidle')
        
        # Find first album and click it
        album_cards = page.locator('.album-card')
        if album_cards.count() > 0:
            print(f"✅ Found {album_cards.count()} albums")
            album_title = album_cards.first.locator('h3').text_content()
            print(f"📂 Opening album: {album_title}")
            
            # Click View Album button instead of the card
            view_btn = album_cards.first.locator('text="View Album"')
            if view_btn.is_visible():
                print("✅ View Album button found, clicking...")
                view_btn.click()
            else:
                album_cards.first.click()
            
            page.wait_for_load_state('networkidle')
            time.sleep(2)  # Extra wait for component loading
            
            # Get page content and look for key elements
            content = page.content()
            
            print("\n📊 Page Content Analysis:")
            print(f"  - Page contains 'upload': {'upload' in content.lower()}")
            print(f"  - Page contains 'photo': {'photo' in content.lower()}")
            print(f"  - Page contains 'drag': {'drag' in content.lower()}")
            print(f"  - Page contains 'app-photo-upload': {'app-photo-upload' in content}")
            
            # Check if upload component is in DOM
            upload_comp = page.locator('app-photo-upload')
            print(f"\n📦 Component Status:")
            print(f"  - app-photo-upload elements: {upload_comp.count()}")
            
            # Check upload-section
            upload_section = page.locator('.upload-section')
            print(f"  - .upload-section elements: {upload_section.count()}")
            
            # Check other sections
            photo_section = page.locator('.photos-section')
            print(f"  - .photos-section elements: {photo_section.count()}")
            
            # Check if there's a loading state
            loading = page.locator('.loading')
            print(f"  - Loading indicator: {loading.count()}")
            
            # Check detail-content visibility
            detail_content = page.locator('.detail-content')
            print(f"  - .detail-content: {detail_content.count()}")
            
            # Look for any buttons
            buttons = page.locator('button')
            print(f"\n🔘 Buttons on page: {buttons.count()}")
            for i in range(min(5, buttons.count())):
                btn_text = buttons.nth(i).text_content()
                print(f"    {i}: {btn_text[:30] if btn_text else '(empty)'}")
            
            # Take screenshot
            page.screenshot(path='/tmp/album-detail-inspect.png')
            print("\n✅ Screenshot saved")
            
            # Print first 3000 chars of body HTML
            body_html = page.locator('body').inner_html()
            print(f"\n📄 Body HTML excerpt (first 2000 chars):\n{body_html[:2000]}")
        else:
            print("❌ No albums found")
        
        browser.close()

if __name__ == '__main__':
    inspect_album_detail()
