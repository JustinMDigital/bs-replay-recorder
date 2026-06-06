// ApplicationLoopback.cpp : This file contains the 'main' function. Program execution begins and ends there.
//

#include <Windows.h>
#include <atomic>
#include <chrono>
#include <iostream>
#include <string>
#include <thread>
#include "LoopbackCapture.h"

void usage()
{
    std::wcout <<
        L"Usage: ProcessLoopbackCapture <pid> <includetree|excludetree> <outputfilename> [maxseconds]\n"
        L"\n"
        L"<pid> is the process ID to capture or exclude from capture\n"
        L"includetree includes audio from that process and its child processes\n"
        L"excludetree includes audio from all processes except that process and its child processes\n"
        L"<outputfilename> is the WAV file to receive the captured audio\n"
        L"[maxseconds] is optional; otherwise capture stops when stdin receives q/quit/stop\n"
        L"\n"
        L"Examples:\n"
        L"\n"
        L"ProcessLoopbackCapture 1234 includetree CapturedAudio.wav\n"
        L"\n"
        L"  Captures audio from process 1234 and its children.\n"
        L"\n"
        L"ProcessLoopbackCapture 1234 excludetree CapturedAudio.wav 30\n"
        L"\n"
        L"  Captures audio from all processes except process 1234 and its children for 30 seconds.\n";
}

int wmain(int argc, wchar_t* argv[])
{
    if (argc != 4 && argc != 5)
    {
        usage();
        return 0;
    }

    DWORD processId = wcstoul(argv[1], nullptr, 0);
    if (processId == 0)
    {
        usage();
        return 0;
    }

    bool includeProcessTree;
    if (wcscmp(argv[2], L"includetree") == 0)
    {
        includeProcessTree = true;
    }
    else if (wcscmp(argv[2], L"excludetree") == 0)
    {
        includeProcessTree = false;
    }
    else
    {
        usage();
        return 0;
    }

    PCWSTR outputFile = argv[3];
    DWORD maxSeconds = 0;
    if (argc == 5)
    {
        maxSeconds = wcstoul(argv[4], nullptr, 0);
    }

    CLoopbackCapture loopbackCapture;
    HRESULT hr = loopbackCapture.StartCaptureAsync(processId, includeProcessTree, outputFile);
    if (FAILED(hr))
    {
        wil::unique_hlocal_string message;
        FormatMessageW(FORMAT_MESSAGE_FROM_SYSTEM | FORMAT_MESSAGE_IGNORE_INSERTS | FORMAT_MESSAGE_ALLOCATE_BUFFER, nullptr, hr,
            MAKELANGID(LANG_NEUTRAL, SUBLANG_DEFAULT), (PWSTR)&message, 0, nullptr);
        std::wcout << L"Failed to start capture\n0x" << std::hex << hr << L": " << message.get() << L"\n";
    }
    else
    {
        std::wcout << L"Capturing process-loopback audio. Send q, quit, or stop on stdin to finish." << std::endl;

        std::atomic_bool stopRequested = false;
        std::thread stdinThread([&stopRequested]()
            {
                std::wstring line;
                while (std::getline(std::wcin, line))
                {
                    if (line == L"q" || line == L"quit" || line == L"stop")
                    {
                        stopRequested = true;
                        break;
                    }
                }
            });

        const auto started = std::chrono::steady_clock::now();
        while (!stopRequested)
        {
            if (maxSeconds > 0)
            {
                const auto elapsed = std::chrono::duration_cast<std::chrono::seconds>(
                    std::chrono::steady_clock::now() - started);
                if (elapsed.count() >= maxSeconds)
                {
                    break;
                }
            }

            Sleep(100);
        }

        loopbackCapture.StopCaptureAsync();
        if (stdinThread.joinable())
        {
            stdinThread.detach();
        }

        std::wcout << L"Finished.\n";
    }

    return 0;
}
