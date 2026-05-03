#!/usr/bin/env python3
"""Inspect the dashboard page structure"""

from playwright.sync_api import sync_playwright

def inspect_dashboard():
    with sync_playwright() as p:
        browser = p.chromium.launch(headless=True)
        page = browser.new_page()
        
        print("Navigating to dashboard...")
        page.goto('http://localhost:4200/dashboard')
        page.wait_for_load_state('networkidle')
        
        # Get page content
        content = page.content()
        
        # Look for buttons
        buttons = page.locator('button')
        print(f"\n✅ Found {buttons.count()} buttons on page")
        
        # Print all button texts
        for i in range(min(10, buttons.count())):
            btn = buttons.nth(i)
            text = btn.text_content()
            print(f"  Button {i}: {text[:50] if text else '(empty)'}")
        
        # Look for text containing "album"
        all_text = page.locator('*')
        print(f"\n✅ Found {all_text.count()} elements on page")
        
        # Check for heading
        h1 = page.locator('h1')
        if h1.count() > 0:
            print(f"\n✅ Page title: {h1.first.text_content()}")
        
        # Check for specific album-related elements
        album_section = page.locator('[class*="album"], [class*="Album"]')
        print(f"\n✅ Found {album_section.count()} album-related elements")
        
        # Take a screenshot for visual inspection
        page.screenshot(path='/tmp/dashboard-inspect.png')
        print("\n✅ Screenshot saved to /tmp/dashboard-inspect.png")
        
        # Print raw HTML of body (first 2000 chars)
        body_html = page.locator('body').inner_html()
        print(f"\n📄 Page HTML (first 1500 chars):\n{body_html[:1500]}")
        
        browser.close()

if __name__ == '__main__':
    inspect_dashboard()
