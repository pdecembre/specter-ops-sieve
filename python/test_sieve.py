import unittest
from sieve import Sieve

class SieveTest(unittest.TestCase):

    def test_sieve_nth_prime(self) -> None:
        sieve = Sieve()
        self.assertEqual(2, sieve.nth_prime(0))
        self.assertEqual(71, sieve.nth_prime(19))
        self.assertEqual(541, sieve.nth_prime(99))
        self.assertEqual(3_581, sieve.nth_prime(500))
        self.assertEqual(7_793, sieve.nth_prime(986))
        self.assertEqual(17_393, sieve.nth_prime(2_000))
        self.assertEqual(15_485_867, sieve.nth_prime(1_000_000))
        self.assertEqual(179_424_691, sieve.nth_prime(10_000_000))
        # self.assertEqual(2_038_074_751, sieve.nth_prime(100_000_000)) not required, just a fun challenge

    def test_sieve_fuzz_nth_prime(self) -> None:
        pass