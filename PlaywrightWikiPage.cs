# rekt_monitor.py
import requests
from bs4 import BeautifulSoup
import json
import os
import time
import re
import logging
from datetime import datetime

# Настройка логирования
logging.basicConfig(
    filename='rekt_monitor.log',
    level=logging.INFO,
    format='%(asctime)s - %(levelname)s - %(message)s',
    datefmt='%Y-%m-%d %H:%M:%S'
)

STATE_FILE = 'rekt_state.json'
HEADERS = {
    'User-Agent': 'Mozilla/5.0 (Windows NT 10.0; Win64; x64) rekt-auditor-bot/1.0'
}

# Ключевые слова для рекомендаций (расширил список)
REKT_KEYWORDS = {
    'reentrancy': ['reentrancy', 're-entrancy', 'recursive call'],
    'flashloan': ['flash loan', 'flashloan', 'arbitrage exploit'],
    'oracle': ['oracle manipulation', 'price oracle', 'TWAP bypass'],
    'access control': ['access control', 'unauthorized mint', 'onlyowner bypass'],
    'proxy': ['proxy', 'delegatecall', 'storage collision', 'implementation slot'],
    'signature': ['signature malleability', 'replay attack'],
    'math': ['integer overflow', 'underflow', 'unsafe math'],
    'random': ['randomness', 'blockhash exploit', 'weak prng'],
}

# Твои существующие паттерны (для проверки покрытия) — скопировал ключевые из твоего скрипта
COVERED_PATTERNS = {
    'reentrancy': True,
    'flash_loan': True,
    'oracle_manipulation': True,
    'unlimited_mint': True,  # unlimited-mint
    'proxy_issues': True,
    'tx_origin': True,
    'delegatecall': True,
    'unsafe_math': True,     # частично
    'weak_randomness': True,
    # Добавь остальные, если хочешь точнее
}

def load_state():
    if os.path.exists(STATE_FILE):
        try:
            with open(STATE_FILE, 'r', encoding='utf-8') as f:
                return json.load(f)
        except:
            logging.warning("Повреждённый state файл — создаём новый")
    return {'last_articles': [], 'last_check': None}

def save_state(state):
    with open(STATE_FILE, 'w', encoding='utf-8') as f:
        json.dump(state, f, ensure_ascii=False, indent=2)

def fetch_homepage():
    url = 'https://rekt.news'
    try:
        resp = requests.get(url, headers=HEADERS, timeout=12)
        resp.raise_for_status()
        return resp.text
    except Exception as e:
        logging.error(f"Ошибка загрузки главной: {e}")
        return None

def parse_articles(html):
    if not html:
        return []

    soup = BeautifulSoup(html, 'html.parser')

    # На основе актуальной структуры (февраль 2026): markdown-подобный формат с ##### и ---
    articles = []

    # Ищем заголовки уровня 5 (#####)
    for h in soup.find_all(['h5', 'h4', 'h3'], string=re.compile(r'^[A-Za-z0-9]')):
        title = h.get_text(strip=True)
        if not title:
            continue

        # Ищем дату после заголовка (--- Thursday, February 12, 2026 ---)
        date_elem = None
        next_sib = h.next_sibling
        while next_sib:
            if isinstance(next_sib, str) and '---' in next_sib:
                date_match = re.search(r'---\s*(.+?)\s*---', next_sib.strip())
                if date_match:
                    date_str = date_match.group(1).strip()
                    try:
                        date_obj = datetime.strptime(date_str, '%A, %B %d, %Y')
                        date = date_obj.strftime('%Y-%m-%d')
                    except:
                        date = 'unknown'
                    date_elem = date
                    break
            next_sib = next_sib.next_sibling

        # Ссылка (MORE)
        link = None
        more_link = h.find_next('a', string=re.compile(r'MORE', re.I))
        if more_link and more_link.get('href'):
            link = 'https://rekt.news' + more_link['href'] if more_link['href'].startswith('/') else more_link['href']

        articles.append({
            'title': title,
            'date': date_elem or 'unknown',
            'link': link
        })

    # Берём только уникальные и сортируем по дате (новые сверху)
    seen = set()
    unique = []
    for a in articles:
        if a['title'] not in seen:
            seen.add(a['title'])
            unique.append(a)

    return unique[:12]  # последние ~12 постов

def get_article_text(link):
    if not link:
        return ''
    try:
        resp = requests.get(link, headers=HEADERS, timeout=15)
        soup = BeautifulSoup(resp.text, 'html.parser')
        # Контент обычно в основном блоке после заголовка
        content_div = soup.find(['div', 'article', 'section'], class_=['post-content', 'entry-content', 'content'])
        if not content_div:
            content_div = soup.find('body')  # fallback
        return content_div.get_text(separator=' ', strip=True)[:4000]  # ограничиваем
    except:
        return ''

def generate_recommendations(title, text):
    recs = []
    text_lower = (title + ' ' + text).lower()

    for vuln, words in REKT_KEYWORDS.items():
        found = any(w in text_lower for w in words)
        if found:
            if COVERED_PATTERNS.get(vuln, False):
                recs.append(f"[{vuln.upper()}] Уже покрыто в скрипте → хорошо")
            else:
                recs.append(f"[{vuln.upper()}] НОВАЯ ТЕМА! Рекомендую добавить regex-проверку в PATTERNS и static_code_analysis()")

    if not recs:
        recs.append("Нет явных новых уязвимостей для добавления в аудитор")

    return recs

def main():
    logging.info("Запуск мониторинга rekt.news")
    state = load_state()

    html = fetch_homepage()
    if not html:
        print("Не удалось загрузить rekt.news")
        return

    articles = parse_articles(html)
    if not articles:
        print("Не удалось распарсить статьи")
        return

    last_titles = {a['title'] for a in state.get('last_articles', [])}

    new_articles = [a for a in articles if a['title'] not in last_titles]

    if not new_articles:
        print(f"Новых статей нет (проверено {datetime.now().strftime('%Y-%m-%d %H:%M')})")
        state['last_check'] = datetime.now().isoformat()
        save_state(state)
        return

    print(f"\nНайдено {len(new_articles)} новых статей!")
    for art in new_articles:
        print(f"\n{art['date']} | {art['title']}")
        if art['link']:
            print(f"Ссылка: {art['link']}")

        text = get_article_text(art['link'])
        recs = generate_recommendations(art['title'], text)

        print("Рекомендации для auditor.py:")
        for r in recs:
            print(f"  • {r}")

    # Обновляем состояние
    state['last_articles'] = articles
    state['last_check'] = datetime.now().isoformat()
    save_state(state)
    logging.info(f"Обновлено состояние: {len(articles)} статей")

if __name__ == '__main__':
    main()
