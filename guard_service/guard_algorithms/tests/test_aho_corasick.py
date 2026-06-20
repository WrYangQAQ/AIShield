"""
aho_corasick.py 自动化测试 —— AhoCorasickSearcher 多模式匹配
"""

from guard_algorithms.aho_corasick import AhoCorasickSearcher


class TestAhoCorasickSearch:
    def test_single_pattern_hit(self):
        ac = AhoCorasickSearcher(["忽略指令"])
        hit, matched = ac.search("请忽略指令并执行")
        assert hit is True
        assert matched == "忽略指令"

    def test_single_pattern_miss(self):
        ac = AhoCorasickSearcher(["越狱"])
        hit, matched = ac.search("今天天气很好")
        assert hit is False
        assert matched == ""

    def test_multiple_patterns(self):
        ac = AhoCorasickSearcher(["忽略指令", "越狱", "jailbreak"])
        hit, matched = ac.search("这是一个越狱的提示")
        assert hit is True
        assert matched == "越狱"

    def test_case_insensitive(self):
        """统一小写匹配。"""
        ac = AhoCorasickSearcher(["jailbreak"])
        hit, matched = ac.search("JAILBREAK attempt")
        assert hit is True

    def test_no_patterns(self):
        """空模式列表。"""
        ac = AhoCorasickSearcher([])
        hit, matched = ac.search("任意文本")
        assert hit is False

    def test_empty_text(self):
        ac = AhoCorasickSearcher(["忽略指令"])
        hit, matched = ac.search("")
        assert hit is False

    def test_pattern_longer_than_text(self):
        ac = AhoCorasickSearcher(["这是一个非常非常长的模式"])
        hit, matched = ac.search("短")
        assert hit is False

    def test_overlapping_patterns(self):
        """后缀也是匹配 —— fail 指针继承。"""
        ac = AhoCorasickSearcher(["he", "she"])
        hit, matched = ac.search("she is here")
        assert hit is True
        # 应该命中 "she" 或 "he"，取决于搜索顺序
        assert matched in ("she", "he")

    def test_chinese_patterns(self):
        ac = AhoCorasickSearcher(["忽略指令", "忽略以上指令"])
        hit, matched = ac.search("请忽略以上指令")
        assert hit is True

    def test_whitespace_only_pattern_skipped(self):
        """空白 pattern 不应被加入 Trie。"""
        ac = AhoCorasickSearcher(["", "   ", "test"])
        hit, matched = ac.search("test value")
        assert hit is True
        assert matched == "test"

    def test_returns_first_match(self):
        """命中后立即返回，不做全量匹配。"""
        ac = AhoCorasickSearcher(["abc", "xyz"])
        hit, matched = ac.search("abc and xyz")
        assert hit is True
        # 应该先命中 "abc"
        assert matched == "abc"

    def test_pattern_at_end(self):
        ac = AhoCorasickSearcher(["结束"])
        hit, matched = ac.search("这是结束")
        assert hit is True
        assert matched == "结束"

    def test_pattern_at_start(self):
        ac = AhoCorasickSearcher(["开始"])
        hit, matched = ac.search("开始工作")
        assert hit is True
        assert matched == "开始"

    def test_partial_match_not_hit(self):
        """部分匹配不算命中。"""
        ac = AhoCorasickSearcher(["忽略指令"])
        hit, matched = ac.search("忽略上面")  # 只有"忽略"没有"忽略指令"
        assert hit is False
