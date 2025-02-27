// Unless explicitly stated otherwise all files in this repository are licensed under the Apache 2 License.
// This product includes software developed at Datadog (https://www.datadoghq.com/). Copyright 2022 Datadog, Inc.

#pragma once
#include <chrono>
#include <forward_list>
#include <list>
#include <memory>
#include <string>
#include <thread>

#include "IService.h"

class Sample;
class IConfiguration;
class IExporter;
class IMetricsSender;
class IProfileFactory;
class ISamplesProvider;


class SamplesAggregator : public IService
{
public:
    SamplesAggregator(IConfiguration* configuration, IExporter* exporter, IMetricsSender* metricsSender);

    // Inherited via IService
    virtual const char* GetName() override;
    virtual bool Start() override;
    virtual bool Stop() override;

    void Register(ISamplesProvider* sampleProvider);

private:
    void Work();
    std::list<Sample> CollectSamples();
    void Export();
    void SendHeartBeatMetric(bool success);

private:
    const char* _serviceName = "SamplesAggregator";
    static const std::chrono::seconds ProcessingInterval;
    static const std::string SuccessfulExportsMetricName;

    std::chrono::seconds _uploadInterval;
    std::chrono::time_point<std::chrono::steady_clock> _nextExportTime;
    std::forward_list<ISamplesProvider*> _samplesProviders;
    IExporter* _exporter;
    std::thread _worker;
    bool _mustStop;
    IMetricsSender* _metricsSender;
};
