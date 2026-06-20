"""
Aho-Corasick 多模式字符串匹配 —— 规则快筛的核心引擎。

为什么选 AC 而不是逐条正则：
  - N 条正则逐条匹配 = O(N×L)，N 大时延迟不可接受
  - AC 自动机 = O(L) + O(匹配数)，近线性时间
  - 字面词条匹配天然不会回溯，没有 ReDoS 风险

构建：Trie 插入 → BFS 构建 Fail 指针 → 搜索时沿 Trie 匹配
"""

from __future__ import annotations

class _Node:
    """
    Trie 节点。
    - next：子节点映射（字符 → 节点索引）
    - fail：失配跳转指针（类似 KMP 的 next 数组）
    - output：当前节点匹配的模式列表
    """

    __slots__ = ("next", "fail", "output")

    def __init__(self) -> None:
        self.next: dict[str, int] = {}
        self.fail: int = 0
        self.output: list[str] = []


class AhoCorasickSearcher:
    """
    Aho-Corasick 多模式字符串匹配器。

    用法：
        searcher = AhoCorasickSearcher(["忽略指令", "越狱", "jailbreak"])
        hit, matched = searcher.search("请忽略指令并执行")
        # hit=True, matched="忽略指令"
    """

    def __init__(self, patterns: list[str] | tuple[str, ...]) -> None:
        """构建 AC 自动机。"""
        self._nodes: list[_Node] = [_Node()]  # 根节点
        for p in patterns:
            if p and p.strip():
                self._add_pattern(p)
        self._build()

    def search(self, text: str) -> tuple[bool, str]:
        """
        搜索文本中是否包含任何已知模式。

        返回：(是否命中, 第一个命中的模式文本)。
        命中后立即返回，不做全量匹配。
        """
        state = 0
        for ch0 in text:
            ch = ch0.lower()  # 统一小写匹配

            # 失配时沿 Fail 指针回跳
            while state != 0 and ch not in self._nodes[state].next:
                state = self._nodes[state].fail

            state = self._nodes[state].next.get(ch, 0)

            if self._nodes[state].output:
                return True, self._nodes[state].output[0]

        return False, ""

    def _add_pattern(self, pattern: str) -> None:
        """将模式插入 Trie 树（统一小写）。"""
        p = pattern.strip().lower()
        state = 0
        for ch in p:
            if ch not in self._nodes[state].next:
                next_idx = len(self._nodes)
                self._nodes[state].next[ch] = next_idx
                self._nodes.append(_Node())
            state = self._nodes[state].next[ch]
        self._nodes[state].output.append(pattern)

    def _build(self) -> None:
        """
        BFS 构建 Fail 指针。

        Fail 指针含义：当前字符无法继续匹配时，跳转到哪个状态继续。
        类似 KMP 的 next 数组，避免从头开始匹配。
        Fail 节点的 Output 也要继承（后缀也是匹配）。
        """
        from collections import deque

        q: deque[int] = deque()

        # 根节点的直接子节点 → Fail 指向根
        for next_idx in self._nodes[0].next.values():
            self._nodes[next_idx].fail = 0
            q.append(next_idx)

        while q:
            r = q.popleft()
            for a, s in self._nodes[r].next.items():
                q.append(s)

                # 沿 r 的 Fail 链找匹配 a 的节点
                state = self._nodes[r].fail
                while state != 0 and a not in self._nodes[state].next:
                    state = self._nodes[state].fail

                self._nodes[s].fail = self._nodes[state].next.get(a, 0)

                # 继承 Fail 节点的 Output
                for o in self._nodes[self._nodes[s].fail].output:
                    self._nodes[s].output.append(o)
